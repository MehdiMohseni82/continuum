using System.Data;
using System.Data.Common;
using Continuum.Core.Contracts;
using Continuum.Core.Data;
using Microsoft.EntityFrameworkCore;

namespace Continuum.Host.Services;

/// <summary>
/// Spend/usage analytics parsed from the token-usage Claude Code records in each assistant event
/// (stored verbatim in Events.RawJson). Cost is an ESTIMATE at standard Anthropic per-tier pricing
/// (input / output / cache-write / cache-read), matched by model family. Uses raw ADO to run the
/// jsonb aggregation directly (EF's SqlQuery composition mangles GROUP BY).
/// </summary>
public sealed class TokenAnalyticsService(ContinuumDbContext db)
{
    // Each SUM cast to bigint so ADO reads it as Int64 (SUM(bigint) is numeric in Postgres).
    private const string Sums =
        "SUM(COALESCE((e.\"RawJson\"->'message'->'usage'->>'input_tokens')::bigint,0))::bigint, " +
        "SUM(COALESCE((e.\"RawJson\"->'message'->'usage'->>'output_tokens')::bigint,0))::bigint, " +
        "SUM(COALESCE((e.\"RawJson\"->'message'->'usage'->>'cache_read_input_tokens')::bigint,0))::bigint, " +
        "SUM(COALESCE((e.\"RawJson\"->'message'->'usage'->>'cache_creation_input_tokens')::bigint,0))::bigint ";
    private const string HasUsage = "e.\"RawJson\"->'message'->'usage' IS NOT NULL";

    public async Task<TokenStatsDto> GetAsync(CancellationToken ct)
    {
        var conn = db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);

        var byModelRaw = await QueryAsync(conn,
            $"SELECT e.\"RawJson\"->'message'->>'model', {Sums} FROM \"Events\" e WHERE {HasUsage} GROUP BY 1",
            r => new ModelRow(Str(r, 0), L(r, 1), L(r, 2), L(r, 3), L(r, 4)), ct);

        var projectRaw = await QueryAsync(conn,
            $"SELECT w.\"DisplayName\", e.\"RawJson\"->'message'->>'model', {Sums} " +
            "FROM \"Events\" e JOIN \"Sessions\" s ON s.\"Id\"=e.\"SessionId\" JOIN \"Workspaces\" w ON w.\"Id\"=s.\"WorkspaceId\" " +
            $"WHERE {HasUsage} GROUP BY 1,2",
            r => new DimRow(Str(r, 0) ?? "(unknown)", Str(r, 1), L(r, 2), L(r, 3), L(r, 4), L(r, 5)), ct);

        var dayRaw = await QueryAsync(conn,
            "SELECT to_char(date_trunc('day', e.\"Timestamp\"),'MM-DD'), e.\"RawJson\"->'message'->>'model', " + Sums +
            $"FROM \"Events\" e WHERE {HasUsage} AND e.\"Timestamp\" >= now() - interval '30 days' GROUP BY 1,2 ORDER BY 1",
            r => new DimRow(Str(r, 0) ?? "", Str(r, 1), L(r, 2), L(r, 3), L(r, 4), L(r, 5)), ct);

        var byModel = byModelRaw
            .Select(r => new ModelUsage(r.Model ?? "(unknown)", r.Input, r.Output, r.CacheRead, r.CacheWrite,
                Cost(r.Input, r.Output, r.CacheWrite, r.CacheRead, r.Model)))
            .OrderByDescending(m => m.CostUsd).ToList();

        var byProject = projectRaw
            .GroupBy(r => r.Label)
            .Select(g => new LabeledCost(g.Key,
                g.Sum(r => Cost(r.Input, r.Output, r.CacheWrite, r.CacheRead, r.Model)),
                g.Sum(r => r.Input + r.Output + r.CacheRead + r.CacheWrite)))
            .OrderByDescending(x => x.CostUsd).Take(12).ToList();

        var perDay = dayRaw
            .GroupBy(r => r.Label)
            .Select(g => new LabeledCost(g.Key,
                g.Sum(r => Cost(r.Input, r.Output, r.CacheWrite, r.CacheRead, r.Model)),
                g.Sum(r => r.Input + r.Output + r.CacheRead + r.CacheWrite)))
            .OrderBy(x => x.Label).ToList();

        return new TokenStatsDto(
            byModel.Sum(m => m.Input), byModel.Sum(m => m.Output),
            byModel.Sum(m => m.CacheRead), byModel.Sum(m => m.CacheWrite),
            byModel.Sum(m => m.CostUsd), byModel, byProject, perDay);
    }

    private static async Task<List<T>> QueryAsync<T>(DbConnection conn, string sql, Func<DbDataReader, T> map, CancellationToken ct)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var list = new List<T>();
        while (await reader.ReadAsync(ct)) list.Add(map(reader));
        return list;
    }

    private static string? Str(DbDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);
    private static long L(DbDataReader r, int i) => r.IsDBNull(i) ? 0 : r.GetInt64(i);

    private static (double In, double Out, double Cw, double Cr) Price(string? model)
    {
        var m = (model ?? "").ToLowerInvariant();
        if (m.Contains("opus")) return (15, 75, 18.75, 1.5);
        if (m.Contains("haiku")) return (1, 5, 1.25, 0.1);
        return (3, 15, 3.75, 0.3); // sonnet / default
    }

    private static double Cost(long input, long output, long cacheWrite, long cacheRead, string? model)
    {
        var p = Price(model);
        return (input * p.In + output * p.Out + cacheWrite * p.Cw + cacheRead * p.Cr) / 1_000_000.0;
    }

    private sealed record ModelRow(string? Model, long Input, long Output, long CacheRead, long CacheWrite);
    private sealed record DimRow(string Label, string? Model, long Input, long Output, long CacheRead, long CacheWrite);
}
