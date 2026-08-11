using Continuum.Core.Access;
using Continuum.Core.Contracts;
using Continuum.Core.Data;
using Continuum.Core.Domain;
using Continuum.Host.Auth;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Continuum.Host.Services;

public sealed class AnalyticsService(ContinuumDbContext db, IAccessPolicy policy)
{
    private sealed record DayCount(DateTime Day, long Count);

    public async Task<AnalyticsDto> GetAsync(CancellationToken ct)
    {
        var visibleSessions = db.Sessions.Where(policy.VisibleSessions());
        var visibleEvents = db.Events.Where(policy.VisibleEvents());
        var visibleMemories = db.Memories.Where(policy.VisibleMemories());

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

        // Grouped rather than counted per workspace, for the reason given in HistoryService.WorkspacesAsync:
        // the visibility rule is an Expression and can't be invoked inside another expression tree.
        var wsCounts = await visibleSessions
            .GroupBy(s => s.Workspace!.DisplayName)
            .Select(g => new CountByLabel(g.Key, g.Count()))
            .ToListAsync(ct);
        var topWorkspaces = wsCounts
            .Where(x => x.Count > 0)
            .OrderByDescending(x => x.Count).Take(8).ToList();

        var memByTypeRaw = await visibleMemories
            .GroupBy(m => m.Type).Select(g => new { g.Key, C = g.Count() }).ToListAsync(ct);
        var memByType = memByTypeRaw.Select(x => new CountByLabel(x.Key.ToString(), x.C)).ToList();

        // Per-day event counts over the last 14 days (raw SQL keeps date bucketing in the DB), scoped
        // to the caller's visible sessions. The scope clause comes from the policy rather than being
        // spelled out here, so it can't drift from the LINQ rules above.
        var cutoff = DateTimeOffset.UtcNow.AddDays(-14);
        var scope = policy.VisibleSessionsSql("s");
        var parameters = new List<NpgsqlParameter> { new("cutoff", cutoff) };
        parameters.AddRange(scope.Args.Select(a => new NpgsqlParameter(a.Name, a.Value)));

        // Concatenated rather than interpolated so this doesn't read as a raw-SQL injection risk: the
        // only spliced text is the policy's own fixed fragment, and every value is a bound parameter.
        var sql =
            @"SELECT date_trunc('day', e.""Timestamp"")::date AS ""Day"", COUNT(*)::bigint AS ""Count""
              FROM ""Events"" e JOIN ""Sessions"" s ON s.""Id"" = e.""SessionId""
              WHERE e.""Timestamp"" >= @cutoff AND " + scope.Sql + @"
              GROUP BY 1 ORDER BY 1";

        var perDayRaw = await db.Database.SqlQueryRaw<DayCount>(sql, [.. parameters]).ToListAsync(ct);
        var perDay = perDayRaw.Select(d => new CountByLabel(d.Day.ToString("MM-dd"), (int)d.Count)).ToList();

        return new AnalyticsDto(sessions, events, memories, agents, handoffs,
            byMachine, byStatus, topWorkspaces, memByType, perDay);
    }
}
