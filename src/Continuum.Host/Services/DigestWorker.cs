using Microsoft.Extensions.Options;

namespace Continuum.Host.Services;

public sealed class DigestOptions
{
    public bool Enabled { get; set; } = true;
    public double IntervalHours { get; set; } = 24;
    /// <summary>Don't post another digest if one was posted within this many hours (survives restarts).</summary>
    public double MinGapHours { get; set; } = 20;
}

/// <summary>Posts a daily activity digest to the "digest" bus channel on a timer.</summary>
public sealed class DigestWorker(
    ILogger<DigestWorker> log,
    IServiceScopeFactory scopes,
    IOptions<DigestOptions> options) : BackgroundService
{
    private readonly DigestOptions _opt = options.Value;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!_opt.Enabled) { log.LogInformation("Daily digest disabled."); return; }

        try { await Task.Delay(TimeSpan.FromMinutes(2), ct); } catch (OperationCanceledException) { return; }

        var interval = TimeSpan.FromHours(Math.Max(0.1, _opt.IntervalHours));
        while (!ct.IsCancellationRequested)
        {
            try { await MaybePostAsync(ct); }
            catch (Exception ex) when (ex is not OperationCanceledException) { log.LogError(ex, "Digest cycle failed"); }
            try { await Task.Delay(interval, ct); } catch (OperationCanceledException) { break; }
        }
    }

    private async Task MaybePostAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var digest = scope.ServiceProvider.GetRequiredService<DigestService>();

        // Skip if a recent digest already exists — prevents spam across frequent redeploys/restarts.
        var latest = await digest.LatestAsync(ct);
        if (latest is not null && DateTimeOffset.UtcNow - latest.CreatedAt < TimeSpan.FromHours(_opt.MinGapHours))
        {
            log.LogInformation("Digest skipped — last one posted {Ago:F1}h ago.",
                (DateTimeOffset.UtcNow - latest.CreatedAt).TotalHours);
            return;
        }

        var posted = await digest.PostDailyAsync(ct);
        log.LogInformation("Posted daily digest: {Sessions} sessions, {Events} events, {Mem} memories.",
            posted.SessionsActive, posted.Events, posted.MemoriesAdded);
    }
}
