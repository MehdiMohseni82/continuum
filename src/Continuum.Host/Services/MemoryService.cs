using Continuum.Core.Contracts;
using Continuum.Core.Data;
using Continuum.Core.Domain;
using Continuum.Core.Embeddings;
using Continuum.Core.Redaction;
using Microsoft.EntityFrameworkCore;
using Pgvector;
using Pgvector.EntityFrameworkCore;

namespace Continuum.Host.Services;

/// <summary>Durable memory: redact → embed → store, and cosine-similarity recall with salience boost.</summary>
public sealed class MemoryService(ContinuumDbContext db, IEmbedder embedder)
{
    public async Task<MemoryDto> SaveAsync(MemorySaveRequest req, CancellationToken ct)
    {
        var redacted = SecretRedactor.Redact(req.Content).Text;
        var embedding = new Vector(await embedder.EmbedAsync(redacted, ct));
        var now = DateTimeOffset.UtcNow;

        var item = new MemoryItem
        {
            Id = Guid.NewGuid(),
            Type = req.Type,
            Content = redacted,
            Embedding = embedding,
            WorkspaceId = req.WorkspaceId,
            SourceSessionId = req.SourceSessionId,
            Pinned = req.Pinned,
            Salience = req.Pinned ? 1f : 0.6f,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Memories.Add(item);
        await db.SaveChangesAsync(ct);
        return ToDto(item, null);
    }

    public async Task<IReadOnlyList<MemoryDto>> SearchAsync(string query, Guid? workspaceId, int take, CancellationToken ct)
    {
        var q = new Vector(await embedder.EmbedAsync(query, ct));

        var baseQuery = db.Memories.Where(m => m.Embedding != null);
        if (workspaceId is { } w)
            baseQuery = baseQuery.Where(m => m.WorkspaceId == w || m.WorkspaceId == null);

        var hits = await baseQuery
            .Select(m => new { Item = m, Distance = m.Embedding!.CosineDistance(q) })
            .OrderBy(x => x.Distance)
            .Take(take)
            .ToListAsync(ct);

        var now = DateTimeOffset.UtcNow;
        foreach (var h in hits)
        {
            h.Item.TimesRecalled++;
            h.Item.LastRecalledAt = now;
            h.Item.Salience = Math.Min(1f, h.Item.Salience + 0.05f); // recall reinforces
        }
        await db.SaveChangesAsync(ct);

        return hits.Select(h => ToDto(h.Item, 1 - h.Distance)).ToList();
    }

    public async Task<IReadOnlyList<MemoryDto>> ListAsync(Guid? workspaceId, MemoryType? type, int take, CancellationToken ct)
    {
        var q = db.Memories.AsQueryable();
        if (workspaceId is { } w) q = q.Where(m => m.WorkspaceId == w);
        if (type is { } t) q = q.Where(m => m.Type == t);

        return await q
            .OrderByDescending(m => m.Pinned).ThenByDescending(m => m.Salience).ThenByDescending(m => m.CreatedAt)
            .Take(take)
            .Select(m => new MemoryDto(m.Id, m.Type, m.Content, m.Salience, m.Pinned, m.WorkspaceId, m.CreatedAt, null))
            .ToListAsync(ct);
    }

    public async Task<bool> ForgetAsync(Guid id, CancellationToken ct)
    {
        var deleted = await db.Memories.Where(m => m.Id == id).ExecuteDeleteAsync(ct);
        return deleted > 0;
    }

    public string EmbedderName => embedder.ProviderName;

    private static MemoryDto ToDto(MemoryItem m, double? score) =>
        new(m.Id, m.Type, m.Content, m.Salience, m.Pinned, m.WorkspaceId, m.CreatedAt, score);
}
