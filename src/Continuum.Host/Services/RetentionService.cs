using Continuum.Core.Contracts;
using Continuum.Core.Data;
using Continuum.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace Continuum.Host.Services;

public sealed class MaintenanceOptions
{
    /// <summary>How often the background worker runs decay. Decay is non-destructive.</summary>
    public double DecayIntervalHours { get; set; } = 6;

    /// <summary>Delete events/sessions older than this many days. 0 = never (default). Destructive when &gt; 0.</summary>
    public int RetentionDays { get; set; } = 0;
}

/// <summary>
/// Age-based purge of old sessions and their events. OFF by default (RetentionDays = 0). Destructive,
/// so it only ever runs when the user explicitly configures a retention window.
/// </summary>
public sealed class RetentionService(ContinuumDbContext db)
{
    public async Task<MaintenanceResult> PurgeOlderThanAsync(int days, CancellationToken ct)
    {
        if (days <= 0) return new MaintenanceResult("retention (disabled)", 0);

        var cutoff = DateTimeOffset.UtcNow.AddDays(-days);
        var oldSessions = await db.Sessions
            .Where(s => s.LastEventAt < cutoff)
            .Select(s => s.Id).ToListAsync(ct);
        if (oldSessions.Count == 0) return new MaintenanceResult("retention", 0);

        await db.Events.Where(e => oldSessions.Contains(e.SessionId)).ExecuteDeleteAsync(ct);
        await db.Checkpoints.Where(c => oldSessions.Contains(c.SessionId)).ExecuteDeleteAsync(ct);
        var removed = await db.Sessions.Where(s => oldSessions.Contains(s.Id)).ExecuteDeleteAsync(ct);
        return new MaintenanceResult("retention", removed);
    }
}
