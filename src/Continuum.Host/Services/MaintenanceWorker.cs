using Microsoft.Extensions.Options;

namespace Continuum.Host.Services;

/// <summary>
/// Periodically runs safe memory decay. Runs age-based retention purge only when the user has
/// explicitly set a retention window (RetentionDays &gt; 0) — never deletes data by default.
/// </summary>
public sealed class MaintenanceWorker(
    ILogger<MaintenanceWorker> log,
    IServiceScopeFactory scopes,
    IOptions<MaintenanceOptions> options) : BackgroundService
{
    private readonly MaintenanceOptions _opt = options.Value;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var interval = TimeSpan.FromHours(Math.Max(0.5, _opt.DecayIntervalHours));

        // Small startup delay so it doesn't fight migrations/first requests.
        try { await Task.Delay(TimeSpan.FromMinutes(1), ct); } catch (OperationCanceledException) { return; }

        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var maintenance = scope.ServiceProvider.GetRequiredService<MemoryMaintenanceService>();
                var decayed = await maintenance.DecayAsync(ct);
                log.LogInformation("Memory decay: {N} adjusted", decayed.Affected);

                if (_opt.RetentionDays > 0)
                {
                    var retention = scope.ServiceProvider.GetRequiredService<RetentionService>();
                    var purged = await retention.PurgeOlderThanAsync(_opt.RetentionDays, ct);
                    if (purged.Affected > 0)
                        log.LogWarning("Retention purge removed {N} sessions older than {D} days", purged.Affected, _opt.RetentionDays);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Maintenance cycle failed");
            }

            try { await Task.Delay(interval, ct); } catch (OperationCanceledException) { break; }
        }
    }
}
