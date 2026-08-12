using Continuum.Core.Contracts;
using Continuum.Core.Data;
using Continuum.Core.Redaction;
using Microsoft.EntityFrameworkCore;

namespace Continuum.Host.Services;

/// <summary>
/// Scans captured transcript events for secrets that landed in the archive (Phase 0 stores raw
/// transcripts verbatim), so you can see what leaked and decide what to do. Read-only awareness.
/// </summary>
public sealed class RedactionReviewService(ContinuumDbContext db, Continuum.Core.Access.IAccessPolicy policy)
{
    public async Task<IReadOnlyList<RedactionHitDto>> ScanAsync(int scanLimit, CancellationToken ct)
    {
        var recent = await db.Events
            .Where(policy.VisibleEvents())
            .Where(e => e.TextExcerpt != null)
            .OrderByDescending(e => e.Id).Take(scanLimit)
            .Select(e => new { e.Id, e.SessionId, e.TextExcerpt, Title = e.Session!.Title })
            .ToListAsync(ct);

        var hits = new List<RedactionHitDto>();
        foreach (var e in recent)
        {
            var labels = SecretRedactor.Detect(e.TextExcerpt);
            if (labels.Count == 0) continue;

            var redacted = SecretRedactor.Redact(e.TextExcerpt!).Text;
            var snippet = redacted.Length > 220 ? redacted[..220] + " …" : redacted;
            hits.Add(new RedactionHitDto(e.SessionId, e.Title, e.Id, labels, snippet));
        }
        return hits;
    }
}
