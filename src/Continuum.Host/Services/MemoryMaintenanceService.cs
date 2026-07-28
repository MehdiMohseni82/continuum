using Continuum.Core.Contracts;
using Continuum.Core.Data;
using Microsoft.EntityFrameworkCore;

namespace Continuum.Host.Services;

/// <summary>
/// Keeps the memory store healthy. Decay is safe (only adjusts salience); dedupe and prune delete
/// and are therefore manual — never run automatically — so the system never destroys your data on its own.
/// </summary>
public sealed class MemoryMaintenanceService(ContinuumDbContext db)
{
    /// <summary>Nudge unpinned, un-recently-recalled memories down in salience. Non-destructive.</summary>
    public async Task<MaintenanceResult> DecayAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var items = await db.Memories.Where(m => !m.Pinned).ToListAsync(ct);

        var changed = 0;
        foreach (var m in items)
        {
            var idleDays = (now - (m.LastRecalledAt ?? m.CreatedAt)).TotalDays;
            if (idleDays < 1) continue;

            var next = Math.Max(0f, m.Salience - 0.02f);
            if (Math.Abs(next - m.Salience) < 0.0001f) continue;

            m.Salience = next;
            m.UpdatedAt = now;
            if (next < 0.05f && m.ExpiresAt is null) m.ExpiresAt = now.AddDays(7); // eligible for prune later
            changed++;
        }
        await db.SaveChangesAsync(ct);
        return new MaintenanceResult("decay", changed);
    }

    /// <summary>Delete near-duplicate memories (same type+scope, cosine distance below threshold), keeping the most salient. Destructive — manual only.</summary>
    public async Task<MaintenanceResult> DedupeAsync(double threshold, CancellationToken ct)
    {
        var items = await db.Memories.Where(m => m.Embedding != null)
            .OrderByDescending(m => m.Salience).ToListAsync(ct);

        var kept = new List<(Guid Wid, Core.Domain.MemoryType Type, float[] Vec)>();
        var removed = 0;

        foreach (var m in items)
        {
            var vec = m.Embedding!.Memory.ToArray();
            var isDup = kept.Any(k =>
                k.Wid == (m.WorkspaceId ?? Guid.Empty) && k.Type == m.Type &&
                CosineDistance(k.Vec, vec) < threshold);

            if (isDup && !m.Pinned)
            {
                db.Memories.Remove(m);
                removed++;
            }
            else
            {
                kept.Add((m.WorkspaceId ?? Guid.Empty, m.Type, vec));
            }
        }
        await db.SaveChangesAsync(ct);
        return new MaintenanceResult("dedupe", removed);
    }

    /// <summary>Delete unpinned memories whose decay expiry has passed. Destructive — manual only.</summary>
    public async Task<MaintenanceResult> PruneAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var removed = await db.Memories
            .Where(m => !m.Pinned && m.ExpiresAt != null && m.ExpiresAt < now)
            .ExecuteDeleteAsync(ct);
        return new MaintenanceResult("prune", removed);
    }

    private static double CosineDistance(float[] a, float[] b)
    {
        double dot = 0, na = 0, nb = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * (double)b[i];
            na += a[i] * (double)a[i];
            nb += b[i] * (double)b[i];
        }
        if (na == 0 || nb == 0) return 1;
        return 1 - dot / (Math.Sqrt(na) * Math.Sqrt(nb));
    }
}
