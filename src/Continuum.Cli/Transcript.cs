using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Continuum.Cli;

/// <summary>
/// Reading a Claude Code session transcript: which room the session is bound to, and what it last
/// said. Separated from <see cref="RelayCommand"/> so it can be tested against fixture transcripts —
/// the hook itself needs a live backend, and this is where the parsing bugs actually live.
/// </summary>
public static partial class Transcript
{
    [GeneratedRegex(@"<<CONTINUUM-ROOM room=([^\s>]+) agent=([^\s>]+) channel=([^\s>]+)>>")]
    private static partial Regex BindMarker();

    [GeneratedRegex(@"<<CONTINUUM-ROOM-LEAVE>>")]
    private static partial Regex LeaveMarker();

    /// <summary>Which room this session is currently bound to, or null if none / left.</summary>
    /// <param name="transcriptText">The whole transcript file, as text.</param>
    public static Bind? CurrentBind(string transcriptText)
    {
        var binds = BindMarker().Matches(transcriptText);
        if (binds.Count == 0) return null;

        // The last bind wins: a session may join, leave, and join a different room.
        var bind = binds[^1];
        var leaves = LeaveMarker().Matches(transcriptText);
        if (leaves.Count > 0 && leaves[^1].Index > bind.Index) return null;

        return Guid.TryParse(bind.Groups[1].Value, out var roomId)
            ? new Bind(roomId, bind.Groups[2].Value, bind.Groups[3].Value)
            : null;
    }

    /// <summary>The last assistant text in the transcript, plus that turn's token usage.</summary>
    public static (string? Text, Usage? Usage) LastAssistantMessage(string transcriptPath)
    {
        string? text = null;
        Usage? usage = null;

        foreach (var line in File.ReadLines(transcriptPath))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            JsonDocument doc;
            try { doc = JsonDocument.Parse(line); } catch (JsonException) { continue; }

            using (doc)
            {
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object) continue;
                var message = root.TryGetProperty("message", out var msg) ? msg : default;

                var role = message.ValueKind == JsonValueKind.Object && message.TryGetProperty("role", out var r)
                    ? r.GetString()
                    : root.TryGetProperty("role", out var r2) ? r2.GetString() : null;
                var type = root.TryGetProperty("type", out var t) ? t.GetString() : null;

                if (role != "assistant" && type != "assistant") continue;

                var content = message.ValueKind == JsonValueKind.Object && message.TryGetProperty("content", out var c)
                    ? c
                    : root.TryGetProperty("content", out var c2) ? c2 : default;

                // Only text blocks: a turn ending in a tool_use block has nothing to say to the room.
                var sb = new StringBuilder();
                if (content.ValueKind == JsonValueKind.String) sb.Append(content.GetString());
                else if (content.ValueKind == JsonValueKind.Array)
                {
                    foreach (var block in content.EnumerateArray())
                    {
                        if (block.ValueKind == JsonValueKind.String) sb.Append(block.GetString());
                        else if (block.ValueKind == JsonValueKind.Object
                                 && block.TryGetProperty("type", out var bt) && bt.GetString() == "text"
                                 && block.TryGetProperty("text", out var btx))
                            sb.Append(btx.GetString());
                    }
                }

                var candidate = sb.ToString().Trim();
                if (candidate.Length == 0) continue;

                text = candidate;
                usage = message.ValueKind == JsonValueKind.Object && message.TryGetProperty("usage", out var u)
                    ? new Usage(
                        Int(u, "input_tokens"), Int(u, "output_tokens"),
                        Int(u, "cache_read_input_tokens"), Int(u, "cache_creation_input_tokens"))
                    : null;
            }
        }

        return (text, usage);

        static int? Int(JsonElement e, string name) =>
            e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : null;
    }

    /// <summary>
    /// `ready` is the responder's handshake into a room — an acknowledgement, not a contribution.
    /// Posting it would make the peer answer a message with no content in it.
    /// </summary>
    public static bool IsReadyHandshake(string? body) =>
        body is not null
        && string.Equals(body.Trim().Trim('*', '_', '`', '"', '\'', ' ', '.', '!'), "ready",
                         StringComparison.OrdinalIgnoreCase);

    public sealed record Bind(Guid RoomId, string Agent, string Channel);

    public sealed record Usage(int? InputTokens, int? OutputTokens, int? CacheReadInputTokens, int? CacheCreationInputTokens);
}

/// <summary>
/// The Stop-hook payload. Claude Code sends snake_case, and <c>JsonSerializerDefaults.Web</c> only
/// relaxes *case*, not word separators — without these attributes every field binds to null and the
/// relay decides "not in a room" for every session, silently and with exit code 0.
/// </summary>
public sealed record HookInput(
    [property: JsonPropertyName("session_id")] string? SessionId,
    [property: JsonPropertyName("transcript_path")] string? TranscriptPath,
    [property: JsonPropertyName("cwd")] string? Cwd)
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static HookInput? Parse(string json)
    {
        try { return JsonSerializer.Deserialize<HookInput>(json, Options); }
        catch (JsonException) { return null; }
    }
}
