using Continuum.Core.Contracts;
using Continuum.Core.Data;
using Microsoft.EntityFrameworkCore;

namespace Continuum.Host.Services;

public sealed class AnalyticsService(ContinuumDbContext db)
{
    private sealed record DayCount(DateTime Day, long Count);

    public async Task<AnalyticsDto> GetAsync(CancellationToken ct)
    {
        var sessions = await db.Sessions.CountAsync(ct);
        var events = await db.Events.CountAsync(ct);
        var memories = await db.Memories.CountAsync(ct);
        var agents = await db.Agents.CountAsync(ct);
        var handoffs = await db.Handoffs.CountAsync(ct);

        var byMachine = (await db.Sessions
            .GroupBy(s => s.Machine!.Name)
            .Select(g => new CountByLabel(g.Key, g.Count()))
            .ToListAsync(ct))
            .OrderByDescending(x => x.Count).ToList();

        var byStatusRaw = await db.Sessions
            .GroupBy(s => s.Status).Select(g => new { g.Key, C = g.Count() }).ToListAsync(ct);
        var byStatus = byStatusRaw.Select(x => new CountByLabel(x.Key.ToString(), x.C)).ToList();

        var topWorkspaces = (await db.Workspaces
            .Select(w => new CountByLabel(w.DisplayName, w.Sessions.Count))
            .ToListAsync(ct))
            .OrderByDescending(x => x.Count).Take(8).ToList();

        var memByTypeRaw = await db.Memories
            .GroupBy(m => m.Type).Select(g => new { g.Key, C = g.Count() }).ToListAsync(ct);
        var memByType = memByTypeRaw.Select(x => new CountByLabel(x.Key.ToString(), x.C)).ToList();

        // Per-day event counts over the last 14 days (raw SQL keeps date bucketing in the DB).
        var cutoff = DateTimeOffset.UtcNow.AddDays(-14);
        var perDayRaw = await db.Database.SqlQuery<DayCount>(
            $@"SELECT date_trunc('day', ""Timestamp"")::date AS ""Day"", COUNT(*)::bigint AS ""Count""
               FROM ""Events"" WHERE ""Timestamp"" >= {cutoff}
               GROUP BY 1 ORDER BY 1").ToListAsync(ct);
        var perDay = perDayRaw.Select(d => new CountByLabel(d.Day.ToString("MM-dd"), (int)d.Count)).ToList();

        return new AnalyticsDto(sessions, events, memories, agents, handoffs,
            byMachine, byStatus, topWorkspaces, memByType, perDay);
    }
}
