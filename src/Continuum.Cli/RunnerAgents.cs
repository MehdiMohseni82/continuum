using System.Text.Json;

namespace Continuum.Cli;

/// <summary>
/// What the daemon's room runner is configured to drive, and what it last did.
///
/// <para>
/// Read from disk rather than asked of the daemon: the CLI deliberately does not reference the daemon
/// project, and a diagnostic that needs the daemon to be answering is useless exactly when the daemon
/// is the problem. The two files are the runner's own — <c>~/Continuum/rooms/agents.json</c> and one
/// <c>&lt;agent&gt;.log</c> per agent, rewritten on every turn.
/// </para>
/// </summary>
public static class RunnerAgents
{
    private static string RoomsDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Continuum", "rooms");

    /// <param name="LastTurn">When the runner last spawned a turn for this agent, from its log's write time.</param>
    public sealed record Entry(string Name, bool Write, string? Role, DateTimeOffset? LastTurn);

    /// <summary>
    /// The configured agents, or an empty list when the runner has nothing to drive — which includes
    /// the seeded example-agent placeholder, since an agent whose path does not exist drives nothing.
    /// </summary>
    public static IReadOnlyList<Entry> Load()
    {
        var path = Path.Combine(RoomsDir, "agents.json");
        if (!File.Exists(path)) return [];

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return [];

            var list = new List<Entry>();
            foreach (var a in doc.RootElement.EnumerateArray())
            {
                if (a.ValueKind != JsonValueKind.Object) continue;

                var name = Str(a, "name");
                var dir = Str(a, "path");
                if (string.IsNullOrWhiteSpace(name)) continue;

                // The seeded placeholder points at /absolute/path/to/a/repo. Treating it as a real
                // agent would report a runner that is driving something when it is driving nothing.
                if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) continue;

                var log = Path.Combine(RoomsDir, $"{name}.log");
                DateTimeOffset? lastTurn = File.Exists(log)
                    ? new DateTimeOffset(File.GetLastWriteTimeUtc(log), TimeSpan.Zero)
                    : null;

                list.Add(new Entry(name!, Bool(a, "write"), Str(a, "role"), lastTurn));
            }
            return list;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>
    /// What the runner recorded about the last turn: posted, no-post, or null when the log predates the
    /// marker. Read rather than inferred — guessing from timestamps was wrong whenever a turn and the
    /// message it produced fell in the same minute.
    /// </summary>
    public static string? LastOutcome(string agent)
    {
        var log = Path.Combine(RoomsDir, $"{agent}.log");
        if (!File.Exists(log)) return null;

        try
        {
            var marker = File.ReadLines(log)
                .Where(l => l.StartsWith("[continuum] outcome=", StringComparison.Ordinal))
                .LastOrDefault();
            return marker?["[continuum] outcome=".Length..].Split(' ')[0];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>The tail of an agent's turn log — what it actually said about its last turn.</summary>
    public static string? LastTurnNote(string agent, int maxChars = 240)
    {
        var log = Path.Combine(RoomsDir, $"{agent}.log");
        if (!File.Exists(log)) return null;

        try
        {
            var text = File.ReadAllText(log).Trim();
            if (text.Length == 0) return null;

            // The log accumulates turns; the last paragraph is the most recent one.
            var paragraphs = text.Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => !x.StartsWith("[continuum] outcome=", StringComparison.Ordinal))
                .ToList();
            if (paragraphs.Count == 0) return null;
            var last = paragraphs[^1];
            last = last.Replace('\n', ' ');
            while (last.Contains("  ")) last = last.Replace("  ", " ");
            return last.Length > maxChars ? string.Concat(last.AsSpan(0, maxChars - 1), "…") : last;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool Bool(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
}
