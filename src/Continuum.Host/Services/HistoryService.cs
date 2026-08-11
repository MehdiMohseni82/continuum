using Continuum.Core.Access;
using Continuum.Core.Contracts;
using Continuum.Core.Data;
using Continuum.Core.Domain;
using Continuum.Core.Embeddings;
using Microsoft.EntityFrameworkCore;
using Pgvector;
using Pgvector.EntityFrameworkCore;

namespace Continuum.Host.Services;

/// <summary>Read-side queries shared by the API and the Blazor UI.</summary>
public sealed class HistoryService(ContinuumDbContext db, IEmbedder embedder, IAccessPolicy policy)
{
    /// <summary>Find sessions by the meaning of their summary (semantic search over SummaryEmbedding).</summary>
    public async Task<IReadOnlyList<SessionSearchHit>> SemanticSessionsAsync(string query, int take, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];
        var q = new Vector(await embedder.EmbedAsync(query, ct));

        return await db.Sessions
            .Where(policy.VisibleSessions())
            .Where(s => s.SummaryEmbedding != null)
            .Select(s => new { s, dist = s.SummaryEmbedding!.CosineDistance(q) })
            .OrderBy(x => x.dist)
            .Take(take)
            .Select(x => new SessionSearchHit(
                x.s.Id, x.s.Title, x.s.Workspace!.DisplayName, x.s.Machine!.Name,
                x.s.Summary, x.s.LastEventAt, x.s.MessageCount, 1 - x.dist))
            .ToListAsync(ct);
    }

    /// <summary>Owner (or admin) toggles whether a session is shared with all users. Returns false if not permitted.</summary>
    public async Task<bool> SetSharedAsync(Guid sessionId, bool shared, CancellationToken ct)
    {
        var updated = await db.Sessions
            .Where(policy.ControlledSessions())
            .Where(s => s.Id == sessionId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Shared, shared), ct);
        return updated > 0;
    }

    /// <summary>Set a workspace's friendly DisplayName. Returns false if no such workspace. Empty names are ignored.</summary>
    public async Task<bool> RenameWorkspaceAsync(Guid id, string displayName, CancellationToken ct)
    {
        var name = displayName?.Trim();
        if (string.IsNullOrEmpty(name)) return false;
        var updated = await db.Workspaces
            .Where(w => w.Id == id)
            .ExecuteUpdateAsync(w => w.SetProperty(x => x.DisplayName, name), ct);
        return updated > 0;
    }

    public async Task<IReadOnlyList<WorkspaceDto>> WorkspacesAsync(CancellationToken ct)
    {
        // Counted by grouping the visible sessions rather than a nested Count() per workspace: the
        // visibility rule is an Expression from the policy, and an expression tree can't invoke another
        // one inline. Grouping keeps a single source of truth, and only workspaces with at least one
        // visible session survive — the same result the old `SessionCount > 0` filter produced.
        var counts = await db.Sessions
            .Where(policy.VisibleSessions())
            .GroupBy(s => s.WorkspaceId)
            .Select(g => new { WorkspaceId = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        if (counts.Count == 0) return [];

        var ids = counts.Select(c => c.WorkspaceId).ToList();
        var workspaces = await db.Workspaces
            .Where(w => ids.Contains(w.Id))
            .Select(w => new { w.Id, w.ProjectKey, w.DisplayName })
            .ToListAsync(ct);

        return [.. workspaces
            .Join(counts, w => w.Id, c => c.WorkspaceId,
                  (w, c) => new WorkspaceDto(w.Id, w.ProjectKey, w.DisplayName, c.Count))
            .OrderBy(w => w.DisplayName)];
    }

    public async Task<IReadOnlyList<SessionSummaryDto>> SessionsAsync(
        Guid? workspaceId, string? q, SessionStatus? status, int skip, int take, CancellationToken ct)
    {
        var query = db.Sessions.Where(policy.VisibleSessions());

        if (workspaceId is { } wid) query = query.Where(s => s.WorkspaceId == wid);
        if (status is { } st) query = query.Where(s => s.Status == st);
        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(s => s.Title != null && EF.Functions.ILike(s.Title, $"%{q}%"));

        return await query
            .OrderByDescending(s => s.LastEventAt)
            .Skip(skip).Take(take)
            .Select(s => new SessionSummaryDto(
                s.Id, s.Title, s.Workspace!.DisplayName, s.Machine!.Name,
                s.Status, s.StartedAt, s.LastEventAt, s.MessageCount))
            .ToListAsync(ct);
    }

    public async Task<SessionDetailDto?> SessionAsync(Guid id, int skip, int take, CancellationToken ct)
    {
        var summary = await db.Sessions
            .Where(policy.VisibleSessions())
            .Where(s => s.Id == id)
            .Select(s => new SessionSummaryDto(
                s.Id, s.Title, s.Workspace!.DisplayName, s.Machine!.Name,
                s.Status, s.StartedAt, s.LastEventAt, s.MessageCount))
            .FirstOrDefaultAsync(ct);

        if (summary is null) return null;

        var events = await db.Events
            .Where(e => e.SessionId == id)
            .OrderBy(e => e.Timestamp).ThenBy(e => e.Id)
            .Skip(skip).Take(take)
            .Select(e => new EventDto(e.Id, e.Uuid, e.Type, e.Role, e.Timestamp, e.TextExcerpt))
            .ToListAsync(ct);

        return new SessionDetailDto(summary, events);
    }

    public async Task<IReadOnlyList<SearchHitDto>> SearchAsync(
        string q, int take, CancellationToken ct,
        Guid? workspaceId = null, string? type = null, int? sinceDays = null)
    {
        if (string.IsNullOrWhiteSpace(q)) return [];

        // PlainToTsQuery must be called inside the expression tree, or EF falls back to client-eval.
        var query = db.Events
            .Where(policy.VisibleEvents())
            .Where(e => e.SearchVector!.Matches(EF.Functions.PlainToTsQuery("english", q)));

        if (workspaceId is { } wid) query = query.Where(e => e.Session!.WorkspaceId == wid);
        if (!string.IsNullOrWhiteSpace(type)) query = query.Where(e => e.Type == type);
        if (sinceDays is { } days)
        {
            var cutoff = DateTimeOffset.UtcNow.AddDays(-days);
            query = query.Where(e => e.Timestamp >= cutoff);
        }

        return await query
            .OrderByDescending(e => e.Timestamp)
            .Take(take)
            .Select(e => new SearchHitDto(
                e.SessionId,
                e.Session!.Title,
                e.Session.Workspace!.DisplayName,
                e.Id,
                e.Type,
                e.Timestamp,
                e.TextExcerpt == null ? null : e.TextExcerpt.Substring(0, e.TextExcerpt.Length > 240 ? 240 : e.TextExcerpt.Length)))
            .ToListAsync(ct);
    }
}
