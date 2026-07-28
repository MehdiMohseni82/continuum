using Continuum.Core.Contracts;
using Continuum.Core.Data;
using Continuum.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace Continuum.Host.Services;

/// <summary>Working-context snapshots taken before compaction or on demand.</summary>
public sealed class CheckpointService(ContinuumDbContext db)
{
    public async Task<CheckpointDto> CreateAsync(CheckpointRequest req, CancellationToken ct)
    {
        var workspaceId = await db.Sessions
            .Where(s => s.Id == req.SessionId)
            .Select(s => (Guid?)s.WorkspaceId)
            .FirstOrDefaultAsync(ct);

        var cp = new Checkpoint
        {
            Id = Guid.NewGuid(),
            SessionId = req.SessionId,
            WorkspaceId = workspaceId,
            Content = req.Content,
            Reason = req.Reason,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Checkpoints.Add(cp);
        await db.SaveChangesAsync(ct);
        return ToDto(cp);
    }

    public async Task<CheckpointDto?> LatestForSessionAsync(Guid sessionId, CancellationToken ct) =>
        await db.Checkpoints
            .Where(c => c.SessionId == sessionId)
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => ToDto(c))
            .FirstOrDefaultAsync(ct);

    public async Task<CheckpointDto?> LatestForWorkspaceAsync(Guid workspaceId, CancellationToken ct) =>
        await db.Checkpoints
            .Where(c => c.WorkspaceId == workspaceId)
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => ToDto(c))
            .FirstOrDefaultAsync(ct);

    private static CheckpointDto ToDto(Checkpoint c) =>
        new(c.Id, c.SessionId, c.Content, c.Reason, c.CreatedAt);
}
