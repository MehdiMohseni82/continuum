using System.Text;
using Continuum.Core.Data;
using Continuum.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace Continuum.Host.Services;

/// <summary>
/// Assembles the text a SessionStart hook injects: the most salient memories for the project
/// (plus global user/feedback memories) and the latest checkpoint, so a new session starts
/// already knowing what matters. Keeps a tight token budget to avoid noise.
/// </summary>
public sealed class HookContextService(ContinuumDbContext db, CheckpointService checkpoints)
{
    public async Task<string> BuildSessionStartAsync(string? projectKey, int maxMemories, CancellationToken ct)
    {
        Guid? workspaceId = null;
        if (!string.IsNullOrWhiteSpace(projectKey))
            workspaceId = await db.Workspaces
                .Where(w => w.ProjectKey == projectKey)
                .Select(w => (Guid?)w.Id)
                .FirstOrDefaultAsync(ct);

        // Project-scoped + global (user/feedback) memories, most salient first.
        var memories = await db.Memories
            .Where(m => m.WorkspaceId == workspaceId
                        || m.WorkspaceId == null
                        || m.Type == MemoryType.User
                        || m.Type == MemoryType.Feedback)
            .OrderByDescending(m => m.Pinned).ThenByDescending(m => m.Salience).ThenByDescending(m => m.CreatedAt)
            .Take(maxMemories)
            .Select(m => new { m.Type, m.Content })
            .ToListAsync(ct);

        var sb = new StringBuilder();
        if (memories.Count > 0)
        {
            sb.AppendLine("## What Continuum remembers");
            foreach (var m in memories)
                sb.Append("- [").Append(m.Type).Append("] ").AppendLine(m.Content);
            sb.AppendLine();
        }

        if (workspaceId is { } wid)
        {
            var cp = await checkpoints.LatestForWorkspaceAsync(wid, ct);
            if (cp is not null)
            {
                sb.AppendLine("## Where you left off (latest checkpoint)");
                sb.AppendLine(cp.Content);
            }
        }

        return sb.Length == 0
            ? "" // nothing to inject yet
            : "Continuum context (auto-injected):\n\n" + sb;
    }
}
