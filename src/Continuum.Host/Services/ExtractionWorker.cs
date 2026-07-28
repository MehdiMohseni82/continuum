using Continuum.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Continuum.Host.Services;

public sealed class ExtractionOptions
{
    public bool Enabled { get; set; } = true;
    public double IntervalMinutes { get; set; } = 3;
    public int PerCycle { get; set; } = 3;
    /// <summary>Skip trivial sessions below this many events.</summary>
    public int MinEvents { get; set; } = 12;
    /// <summary>Only extract sessions idle at least this long, so active ones aren't processed mid-flight.</summary>
    public int IdleMinutes { get; set; } = 10;
}

/// <summary>
/// Background "dreaming": finds idle, not-yet-processed sessions and extracts durable memories from
/// them. Marks a session ExtractedAt only on success, so a not-yet-ready model just retries later.
/// </summary>
public sealed class ExtractionWorker(
    ILogger<ExtractionWorker> log,
    IServiceScopeFactory scopes,
    IOptions<ExtractionOptions> options) : BackgroundService
{
    private readonly ExtractionOptions _opt = options.Value;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!_opt.Enabled) { log.LogInformation("Auto-memory extraction disabled."); return; }

        try { await Task.Delay(TimeSpan.FromMinutes(1), ct); } catch (OperationCanceledException) { return; }

        var interval = TimeSpan.FromMinutes(Math.Max(0.5, _opt.IntervalMinutes));
        while (!ct.IsCancellationRequested)
        {
            try { await RunCycleAsync(ct); }
            catch (Exception ex) when (ex is not OperationCanceledException) { log.LogError(ex, "Extraction cycle failed"); }
            try { await Task.Delay(interval, ct); } catch (OperationCanceledException) { break; }
        }
    }

    private async Task RunCycleAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ContinuumDbContext>();
        var extract = scope.ServiceProvider.GetRequiredService<MemoryExtractionService>();

        var cutoff = DateTimeOffset.UtcNow.AddMinutes(-_opt.IdleMinutes);
        var batch = await db.Sessions
            .Where(s => s.ExtractedAt == null && s.MessageCount >= _opt.MinEvents && s.LastEventAt < cutoff)
            .OrderByDescending(s => s.LastEventAt)
            .Take(_opt.PerCycle)
            .ToListAsync(ct);

        if (batch.Count == 0) return;

        foreach (var session in batch)
        {
            try
            {
                await extract.ExtractAsync(session.Id, ct);
                session.ExtractedAt = DateTimeOffset.UtcNow;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Model likely not reachable/pulled yet — stop this cycle and retry next time.
                log.LogWarning(ex, "Extraction unavailable; will retry (session {Session})", session.Id);
                break;
            }
        }
        await db.SaveChangesAsync(ct);
    }
}
