using System.Text.Json;

namespace Continuum.Core.Generation;

/// <summary>
/// Reading JSON back out of a language model, tolerantly. Models wrap objects in ``` fences, prefix
/// them with a sentence of preamble, or return an array where an object was asked for — none of which
/// should cost the caller the whole response. Nothing here throws.
/// </summary>
internal static class LlmJson
{
    /// <summary>The first JSON value in the text, or null if there isn't one.</summary>
    public static JsonElement? Root(string? raw)
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

    public static string Unfence(string s)
    {
        if (!s.Contains("```")) return s;
        var lines = s.Split('\n');
        return string.Join('\n', lines.Where(l => !l.TrimStart().StartsWith("```")));
    }

    public static string? GetString(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    public static bool GetBool(JsonElement e, string name, bool fallback = false) =>
        e.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? v.GetBoolean() : fallback;

    /// <summary>Trimmed, capped, and null when it holds nothing worth keeping.</summary>
    public static string? Clean(string? s, int max)
    {
        var t = s?.Trim();
        if (string.IsNullOrEmpty(t)) return null;
        return t.Length > max ? t[..max] : t;
    }
}
