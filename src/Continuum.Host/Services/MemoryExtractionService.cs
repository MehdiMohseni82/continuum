using System.Text;
using Continuum.Core.Contracts;
using Continuum.Core.Data;
using Continuum.Core.Embeddings;
using Continuum.Core.Generation;
using Microsoft.EntityFrameworkCore;
using Pgvector;

namespace Continuum.Host.Services;

/// <summary>
/// "Dreaming": from each session's transcript, writes a short summary (embedded for semantic session
/// search) and distills durable, reusable facts into memories — one local-LLM call does both.
/// Memory content is redacted + deduped on save, so the store fills itself without flooding.
/// </summary>
public sealed class MemoryExtractionService(
    ContinuumDbContext db,
    IChatCompleter chat,
    MemoryService memory,
    IEmbedder embedder,
    ILogger<MemoryExtractionService> log)
{
    private const int MaxDigestChars = 12_000;

    private const string System =
        "You process a coding session transcript. Do two things: (1) write a 2-3 sentence SUMMARY of what " +
        "the session was about, key decisions, and the outcome; (2) distill a few DURABLE, reusable facts " +
        "worth remembering long-term — user preferences/style (User/Feedback), project decisions, architecture, " +
        "conventions, gotchas (Project), and useful links (Reference). Ignore ephemeral task chatter and anything " +
        "obvious from the code. Prefer 0-6 high-signal memories; none if nothing is durable.";

    /// <summary>Extract summary + memories for one session. Returns how many new memories were saved.</summary>
    public async Task<int> ExtractAsync(Guid sessionId, CancellationToken ct)
    {
        var session = await db.Sessions.FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session is null) return 0;
        var workspaceId = (Guid?)session.WorkspaceId;

        var events = await db.Events
            .Where(e => e.SessionId == sessionId && e.TextExcerpt != null && (e.Role == "user" || e.Role == "assistant"))
            .OrderBy(e => e.Timestamp).ThenBy(e => e.Id)
            .Select(e => new { e.Role, e.TextExcerpt })
            .ToListAsync(ct);

        if (events.Count == 0) return 0;

        var digest = BuildDigest(events.Select(e => (e.Role!, e.TextExcerpt!)));
        var user =
            "Transcript (most recent turns):\n\n" + digest +
            "\n\nReturn JSON: {\"summary\":\"2-3 sentences\",\"memories\":[{\"type\":\"User|Feedback|Project|Reference\",\"content\":\"one durable fact\"}]}";

        // Let LLM/connection failures propagate so the worker can retry (e.g. model not pulled yet).
        var json = await chat.CompleteAsync(System, user, jsonMode: true, ct);
        var parsed = ExtractionParser.ParseFull(json);

        if (!string.IsNullOrWhiteSpace(parsed.Summary))
        {
            session.Summary = parsed.Summary;
            session.SummaryEmbedding = new Vector(await embedder.EmbedAsync(parsed.Summary, ct));
        }

        var saved = 0;
        foreach (var c in parsed.Memories)
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

        await db.SaveChangesAsync(ct); // persist the summary + embedding (and mark work done)
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
