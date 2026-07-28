using Continuum.Core.Contracts;
using Continuum.Core.Data;
using Continuum.Core.Domain;
using Continuum.Host.Auth;
using Microsoft.EntityFrameworkCore;

namespace Continuum.Host.Services;

public sealed class AnalyticsService(ContinuumDbContext db, ICurrentUser current)
{
    private sealed record DayCount(DateTime Day, long Count);

    public async Task<AnalyticsDto> GetAsync(CancellationToken ct)
    {
        var admin = current.IsAdmin;
        var uid = current.UserId;

        var visibleSessions = db.Sessions.Where(s => admin || s.OwnerId == uid || s.Shared);
        var visibleEvents = db.Events.Where(e => admin || e.Session!.OwnerId == uid || e.Session.Shared);
        var visibleMemories = db.Memories.Where(m => admin || m.OwnerId == uid || m.Shared);

        var sessions = await visibleSessions.CountAsync(ct);
        var events = await visibleEvents.CountAsync(ct);
        var memories = await visibleMemories.CountAsync(ct);
        var agents = await db.Agents.CountAsync(ct);       // bus is shared infra
        var handoffs = await db.Handoffs.CountAsync(ct);

        var byMachine = (await visibleSessions
            .GroupBy(s => s.Machine!.Name)
            .Select(g => new CountByLabel(g.Key, g.Count()))
            .ToListAsync(ct))
            .OrderByDescending(x => x.Count).ToList();

        var byStatusRaw = await visibleSessions
            .GroupBy(s => s.Status).Select(g => new { g.Key, C = g.Count() }).ToListAsync(ct);
        var byStatus = byStatusRaw.Select(x => new CountByLabel(x.Key.ToString(), x.C)).ToList();

        var topWorkspaces = (await db.Workspaces
            .Select(w => new CountByLabel(w.DisplayName, w.Sessions.Count(s => admin || s.OwnerId == uid || s.Shared)))
            .ToListAsync(ct))
            .Where(x => x.Count > 0)
            .OrderByDescending(x => x.Count).Take(8).ToList();

        var memByTypeRaw = await visibleMemories
            .GroupBy(m => m.Type).Select(g => new { g.Key, C = g.Count() }).ToListAsync(ct);
        var memByType = memByTypeRaw.Select(x => new CountByLabel(x.Key.ToString(), x.C)).ToList();

        // Per-day event counts over the last 14 days (raw SQL keeps date bucketing in the DB),
        // scoped to the caller's visible sessions.
        var cutoff = DateTimeOffset.UtcNow.AddDays(-14);
        var ownerFilter = uid ?? Guid.Empty;
        var perDayRaw = await db.Database.SqlQuery<DayCount>(
            $@"SELECT date_trunc('day', e.""Timestamp"")::date AS ""Day"", COUNT(*)::bigint AS ""Count""
               FROM ""Events"" e JOIN ""Sessions"" s ON s.""Id"" = e.""SessionId""
               WHERE e.""Timestamp"" >= {cutoff}
                 AND ({admin} OR s.""OwnerId"" = {ownerFilter} OR s.""Shared"")
               GROUP BY 1 ORDER BY 1").ToListAsync(ct);
        var perDay = perDayRaw.Select(d => new CountByLabel(d.Day.ToString("MM-dd"), (int)d.Count)).ToList();

        return new AnalyticsDto(sessions, events, memories, agents, handoffs,
            byMachine, byStatus, topWorkspaces, memByType, perDay);
    }
}
