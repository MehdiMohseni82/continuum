using System.Text;
using Continuum.Core.Contracts;
using Continuum.Core.Data;
using Continuum.Core.Export;
using Microsoft.EntityFrameworkCore;

namespace Continuum.Host.Services;

/// <summary>
/// Cross-machine continuity: reconstructs a session's transcript from stored events so it can be
/// materialized as a JSONL file on another machine and resumed, and produces a markdown hand-off.
/// </summary>
public sealed class ResumeService(ContinuumDbContext db, HistoryService history, Continuum.Host.Auth.ICurrentUser current)
{
    /// <summary>
    /// Rebuild the raw JSONL (one stored line per event, in order). Note: lines round-trip through
    /// jsonb, so whitespace/key-order is normalized — semantically intact, not byte-identical.
    /// </summary>
    public async Task<string?> ExportJsonlAsync(Guid sessionId, CancellationToken ct)
    {
        var admin = current.IsAdmin;
        var uid = current.UserId;
        var visible = await db.Sessions.AnyAsync(
            s => s.Id == sessionId && (admin || s.OwnerId == uid || s.Shared), ct);
        if (!visible) return null;

        var lines = await db.Events
            .Where(e => e.SessionId == sessionId)
            .OrderBy(e => e.Timestamp).ThenBy(e => e.Id)
            .Select(e => e.RawJson)
            .ToListAsync(ct);

        var sb = new StringBuilder();
        foreach (var line in lines)
            sb.Append(line).Append('\n');
        return sb.ToString();
    }

    public async Task<string?> BundleMarkdownAsync(Guid sessionId, CancellationToken ct)
    {
        var detail = await history.SessionAsync(sessionId, 0, 1000, ct);
        return detail is null ? null : ResumeBundle.ToMarkdown(detail.Session, detail.Events);
    }

    /// <summary>The path a resumed file should live at on a target machine, given that machine's project key.</summary>
    public static string ResumeRelativePath(string projectKey, Guid sessionId) =>
        $"projects/{projectKey}/{sessionId}.jsonl";
}
