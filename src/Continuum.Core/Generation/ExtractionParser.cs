using System.Text.Json;
using Continuum.Core.Domain;

namespace Continuum.Core.Generation;

public sealed record ExtractedMemory(MemoryType Type, string Content);
public sealed record ExtractionResult(string? Summary, IReadOnlyList<ExtractedMemory> Memories);

/// <summary>
/// Parses the LLM's extraction output — a session summary plus durable memory candidates. Tolerant:
/// accepts {"summary":…,"memories":[…]}, a bare memories array, or ``` fences, and never throws.
/// </summary>
public static class ExtractionParser
{
    private const int MaxContent = 1000;
    private const int MaxSummary = 800;

    /// <summary>Back-compat: just the memories.</summary>
    public static IReadOnlyList<ExtractedMemory> Parse(string? raw, int maxItems = 8) =>
        ParseFull(raw, maxItems).Memories;

    public static ExtractionResult ParseFull(string? raw, int maxItems = 8)
    {
        var root = Root(raw);
        if (root is not { } r) return new ExtractionResult(null, []);

        string? summary = null;
        JsonElement array;

        if (r.ValueKind == JsonValueKind.Array)
        {
            array = r;
        }
        else if (r.ValueKind == JsonValueKind.Object)
        {
            summary = GetString(r, "summary")?.Trim();
            if (summary is { Length: > MaxSummary }) summary = summary[..MaxSummary];
            if (string.IsNullOrWhiteSpace(summary)) summary = null;

            if (!(r.TryGetProperty("memories", out array) && array.ValueKind == JsonValueKind.Array))
                return new ExtractionResult(summary, []);
        }
        else
        {
            return new ExtractionResult(null, []);
        }

        var result = new List<ExtractedMemory>();
        foreach (var item in array.EnumerateArray())
        {
            if (result.Count >= maxItems) break;
            if (item.ValueKind != JsonValueKind.Object) continue;

            var content = GetString(item, "content")?.Trim();
            if (string.IsNullOrWhiteSpace(content)) continue;
            if (content.Length > MaxContent) content = content[..MaxContent];

            var type = Enum.TryParse<MemoryType>(GetString(item, "type"), ignoreCase: true, out var t) ? t : MemoryType.Project;
            result.Add(new ExtractedMemory(type, content));
        }
        return new ExtractionResult(summary, result);
    }

    private static JsonElement? Root(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var text = Unfence(raw).Trim();
        var start = text.IndexOfAny(['{', '[']);
        if (start < 0) return null;
        text = text[start..];
        try
        {
            using var doc = JsonDocument.Parse(text);
            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Unfence(string s)
    {
        if (!s.Contains("```")) return s;
        var lines = s.Split('\n');
        return string.Join('\n', lines.Where(l => !l.TrimStart().StartsWith("```")));
    }

    private static string? GetString(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
