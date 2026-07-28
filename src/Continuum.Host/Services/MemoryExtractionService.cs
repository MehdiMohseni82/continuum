using System.Text;
using Continuum.Core.Contracts;
using Continuum.Core.Data;
using Continuum.Core.Generation;
using Microsoft.EntityFrameworkCore;

namespace Continuum.Host.Services;

/// <summary>
/// "Dreaming": distills durable, reusable facts from a session's transcript into memories using the
/// local LLM. Content is redacted + deduped on save, so the store fills itself without flooding.
/// </summary>
public sealed class MemoryExtractionService(
    ContinuumDbContext db,
    IChatCompleter chat,
    MemoryService memory,
    ILogger<MemoryExtractionService> log)
{
    private const int MaxDigestChars = 12_000;

    private const string System =
        "You distill a coding session transcript into a few DURABLE, reusable facts worth remembering " +
        "long-term across future sessions. Capture: user preferences and working style (User/Feedback), " +
        "project decisions, architecture, conventions, and gotchas (Project), and useful links or resource " +
        "pointers (Reference). Ignore ephemeral task chatter, one-off debugging, and anything already obvious " +
        "from the code. Prefer 0-6 high-signal items; return none if nothing is durable.";

    /// <summary>Extract memories for one session. Returns how many new memories were saved.</summary>
    public async Task<int> ExtractAsync(Guid sessionId, CancellationToken ct)
    {
        var workspaceId = await db.Sessions.Where(s => s.Id == sessionId)
            .Select(s => (Guid?)s.WorkspaceId).FirstOrDefaultAsync(ct);

        var events = await db.Events
            .Where(e => e.SessionId == sessionId && e.TextExcerpt != null && (e.Role == "user" || e.Role == "assistant"))
            .OrderBy(e => e.Timestamp).ThenBy(e => e.Id)
            .Select(e => new { e.Role, e.TextExcerpt })
            .ToListAsync(ct);

        if (events.Count == 0) return 0;

        var digest = BuildDigest(events.Select(e => (e.Role!, e.TextExcerpt!)));
        var user =
            "Transcript (most recent turns):\n\n" + digest +
            "\n\nReturn JSON: {\"memories\":[{\"type\":\"User|Feedback|Project|Reference\",\"content\":\"one durable fact\"}]}";

        // Let LLM/connection failures propagate so the worker can retry (e.g. model not pulled yet).
        var json = await chat.CompleteAsync(System, user, jsonMode: true, ct);
        var candidates = ExtractionParser.Parse(json);
        var saved = 0;
        foreach (var c in candidates)
        {
            var req = new MemorySaveRequest
            {
                Type = c.Type,
                Content = c.Content,
                WorkspaceId = workspaceId,
                SourceSessionId = sessionId,
            };
            // Skip near-duplicates of what we already know.
            if (await memory.SaveDistinctAsync(req, duplicateThreshold: 0.12, ct) is not null)
                saved++;
        }

        if (saved > 0) log.LogInformation("Extracted {N} memories from session {Session}", saved, sessionId);
        return saved;
    }

    private static string BuildDigest(IEnumerable<(string Role, string Text)> turns)
    {
        // Keep the most recent content within the budget (conclusions/decisions cluster late).
        var lines = turns.Select(t => $"{(t.Role == "user" ? "User" : "Assistant")}: {t.Text}").ToList();
        var sb = new StringBuilder();
        for (var i = lines.Count - 1; i >= 0; i--)
        {
            if (sb.Length + lines[i].Length > MaxDigestChars) break;
            sb.Insert(0, lines[i] + "\n");
        }
        return sb.ToString();
    }
}
