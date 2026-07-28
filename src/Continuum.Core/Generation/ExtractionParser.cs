using System.Text.Json;
using Continuum.Core.Domain;

namespace Continuum.Core.Generation;

public sealed record ExtractedMemory(MemoryType Type, string Content);

/// <summary>
/// Parses the LLM's memory-extraction output into typed candidates. Tolerant: accepts a
/// {"memories":[…]} object or a bare array, strips ``` fences, and never throws on junk.
/// </summary>
public static class ExtractionParser
{
    private const int MaxContent = 1000;

    public static IReadOnlyList<ExtractedMemory> Parse(string? raw, int maxItems = 8)
    {
        if (string.IsNullOrWhiteSpace(raw)) return [];

        var text = Unfence(raw).Trim();
        // Find the outermost JSON value.
        var start = text.IndexOfAny(['{', '[']);
        if (start < 0) return [];
        text = text[start..];

        JsonElement root;
        try
        {
            using var doc = JsonDocument.Parse(text);
            root = doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return [];
        }

        JsonElement array;
        if (root.ValueKind == JsonValueKind.Array)
            array = root;
        else if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("memories", out var m) && m.ValueKind == JsonValueKind.Array)
            array = m;
        else
            return [];

        var result = new List<ExtractedMemory>();
        foreach (var item in array.EnumerateArray())
        {
            if (result.Count >= maxItems) break;
            if (item.ValueKind != JsonValueKind.Object) continue;

            var content = GetString(item, "content")?.Trim();
            if (string.IsNullOrWhiteSpace(content)) continue;
            if (content.Length > MaxContent) content = content[..MaxContent];

            var typeStr = GetString(item, "type");
            var type = Enum.TryParse<MemoryType>(typeStr, ignoreCase: true, out var t) ? t : MemoryType.Project;

            result.Add(new ExtractedMemory(type, content));
        }
        return result;
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
