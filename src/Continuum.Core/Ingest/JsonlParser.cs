using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Continuum.Core.Contracts;

namespace Continuum.Core.Ingest;

/// <summary>
/// Turns a raw Claude Code JSONL line into an <see cref="IngestEvent"/>.
/// Deliberately defensive: it only reaches for fields it recognizes and never throws on an
/// unexpected shape. An invalid or unknown line is still preserved verbatim so nothing is lost.
/// </summary>
public static class JsonlParser
{
    private const int MaxExcerpt = 8_000;

    /// <summary>
    /// Parse a single line. Returns null for blank lines. <paramref name="fallbackSessionId"/>
    /// (from the file name) is used when the line itself doesn't name the session.
    /// </summary>
    public static IngestEvent? ParseLine(string line, Guid fallbackSessionId, string projectKey)
    {
        if (string.IsNullOrWhiteSpace(line))
            return null;

        JsonElement raw;
        JsonElement root;
        try
        {
            using var doc = JsonDocument.Parse(line);
            root = doc.RootElement.Clone();
            raw = root;
        }
        catch (JsonException)
        {
            // Not valid JSON — keep the text so it is never silently dropped.
            var wrapped = JsonSerializer.SerializeToElement(new { _unparsed = line });
            return new IngestEvent
            {
                SessionId = fallbackSessionId,
                ProjectKey = projectKey,
                Uuid = DeterministicGuid(fallbackSessionId, line),
                Type = "unparsed",
                Timestamp = DateTimeOffset.UtcNow,
                Raw = wrapped,
            };
        }

        var type = GetString(root, "type") ?? "unknown";
        var sessionId = GetGuid(root, "sessionId") ?? fallbackSessionId;
        var uuid = GetGuid(root, "uuid") ?? DeterministicGuid(sessionId, line);
        var text = ExtractText(root);

        return new IngestEvent
        {
            SessionId = sessionId,
            ProjectKey = projectKey,
            Uuid = uuid,
            ParentUuid = GetGuid(root, "parentUuid"),
            Type = type,
            Role = GetRole(root),
            Timestamp = GetTimestamp(root),
            Text = text,
            CcVersion = GetString(root, "version"),
            GitBranch = GetString(root, "gitBranch"),
            Title = type is "ai-title" ? GetString(root, "aiTitle") ?? GetString(root, "slug") : null,
            Raw = raw,
        };
    }

    /// <summary>A stable GUID derived from the session and the raw line, for lines with no uuid.</summary>
    public static Guid DeterministicGuid(Guid sessionId, string line)
    {
        var bytes = Encoding.UTF8.GetBytes(sessionId.ToString("N") + "|" + line);
        var hash = SHA256.HashData(bytes);
        return new Guid(hash.AsSpan(0, 16));
    }

    private static string? GetRole(JsonElement root)
    {
        if (root.TryGetProperty("message", out var msg) && msg.ValueKind == JsonValueKind.Object &&
            msg.TryGetProperty("role", out var role) && role.ValueKind == JsonValueKind.String)
            return role.GetString();
        return GetString(root, "userType");
    }

    private static DateTimeOffset GetTimestamp(JsonElement root)
    {
        if (root.TryGetProperty("timestamp", out var ts) && ts.ValueKind == JsonValueKind.String &&
            DateTimeOffset.TryParse(ts.GetString(), out var parsed))
            return parsed;
        return DateTimeOffset.UtcNow;
    }

    /// <summary>Flatten whatever human-readable text the line carries, for display + search.</summary>
    private static string? ExtractText(JsonElement root)
    {
        var sb = new StringBuilder();

        if (root.TryGetProperty("message", out var msg) && msg.ValueKind == JsonValueKind.Object)
            AppendContent(msg, sb);

        // Some line types carry a top-level string field instead of a message object.
        foreach (var key in new[] { "content", "lastPrompt", "aiTitle", "summary" })
            if (sb.Length == 0 && root.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String)
                sb.Append(v.GetString());

        var text = sb.ToString().Trim();
        if (text.Length == 0) return null;
        return text.Length > MaxExcerpt ? text[..MaxExcerpt] : text;
    }

    private static void AppendContent(JsonElement message, StringBuilder sb)
    {
        if (!message.TryGetProperty("content", out var content))
            return;

        switch (content.ValueKind)
        {
            case JsonValueKind.String:
                Append(sb, content.GetString());
                break;
            case JsonValueKind.Array:
                foreach (var block in content.EnumerateArray())
                    AppendBlock(block, sb);
                break;
        }
    }

    private static void AppendBlock(JsonElement block, StringBuilder sb)
    {
        if (block.ValueKind == JsonValueKind.String) { Append(sb, block.GetString()); return; }
        if (block.ValueKind != JsonValueKind.Object) return;

        // Plain text blocks.
        if (block.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String)
            Append(sb, t.GetString());

        // tool_result blocks may nest content recursively.
        if (block.TryGetProperty("content", out var nested))
        {
            if (nested.ValueKind == JsonValueKind.String) Append(sb, nested.GetString());
            else if (nested.ValueKind == JsonValueKind.Array)
                foreach (var inner in nested.EnumerateArray())
                    AppendBlock(inner, sb);
        }
    }

    private static void Append(StringBuilder sb, string? s)
    {
        if (string.IsNullOrEmpty(s)) return;
        if (sb.Length > 0) sb.Append('\n');
        sb.Append(s);
    }

    private static string? GetString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static Guid? GetGuid(JsonElement root, string name) =>
        root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String &&
        Guid.TryParse(v.GetString(), out var g) ? g : null;
}
