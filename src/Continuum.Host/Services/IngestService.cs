using Continuum.Core.Access;
using Continuum.Core.Contracts;
using Continuum.Core.Data;
using Continuum.Core.Domain;
using Continuum.Host.Auth;
using Microsoft.EntityFrameworkCore;

namespace Continuum.Host.Services;

/// <summary>Upserts machine/workspace/session and inserts new events. Idempotent on (SessionId, Uuid).</summary>
public sealed class IngestService(ContinuumDbContext db, ICurrentUser current, IAccessPolicy policy)
{
    private Guid Org => policy.WriteOrgId;

    // Postgres cannot store the NUL character in jsonb (error 22P05) or text columns. Transcripts can
    // contain it via captured terminal/binary output, so it is stripped before storage — both the raw
    // char and the six-char escape sequence. Built from char codes to avoid source-escaping pitfalls.
    private static readonly string NulChar = ((char)0).ToString();
    private static readonly string NulEscape = (char)92 + "u0000"; // backslash + u0000

    public async Task<IngestResult> IngestAsync(IngestBatch batch, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        var machine = await db.Machines.FirstOrDefaultAsync(m => m.Name == batch.MachineName, ct);
        if (machine is null)
        {
            machine = new Machine { Id = Guid.NewGuid(), Name = batch.MachineName };
            db.Machines.Add(machine);
        }
        machine.LastSeenAt = now;

        var inserted = 0;
        var duplicates = 0;

        foreach (var group in batch.Events.GroupBy(e => e.SessionId))
        {
            var projectKey = group.First().ProjectKey;

            var workspace = await db.Workspaces.FirstOrDefaultAsync(w => w.OrgId == Org && w.ProjectKey == projectKey, ct);
            if (workspace is null)
            {
                workspace = new Workspace
                {
                    Id = Guid.NewGuid(),
                    OrgId = Org,
                    ProjectKey = projectKey,
                    DisplayName = WorkspaceNaming.Prettify(projectKey),
                    FirstSeenAt = now,
                };
                db.Workspaces.Add(workspace);
            }

            var session = await db.Sessions.FirstOrDefaultAsync(s => s.Id == group.Key, ct);
            if (session is null)
            {
                var first = group.Min(e => e.Timestamp);
                session = new Session
                {
                    Id = group.Key,
                    MachineId = machine.Id,
                    WorkspaceId = workspace.Id,
                    OrgId = Org,
                    // Attribute the session to whoever's token ingested it (the legacy token → admin).
                    OwnerId = current.UserId ?? Defaults.DefaultOwnerId,
                    StartedAt = first,
                    LastEventAt = first,
                };
                db.Sessions.Add(session);
            }

            // Idempotency: which of these uuids already exist for this session?
            var incoming = group.Select(e => e.Uuid).ToList();
            var known = await db.Events
                .Where(x => x.SessionId == group.Key && incoming.Contains(x.Uuid))
                .Select(x => x.Uuid)
                .ToHashSetAsync(ct);

            foreach (var e in group.OrderBy(e => e.Timestamp))
            {
                if (!known.Add(e.Uuid))
                {
                    duplicates++;
                    continue;
                }

                db.Events.Add(new Event
                {
                    SessionId = e.SessionId,
                    Uuid = e.Uuid,
                    ParentUuid = e.ParentUuid,
                    Type = e.Type,
                    Role = e.Role,
                    Timestamp = e.Timestamp,
                    TextExcerpt = StripNul(e.Text),
                    RawJson = StripNul(e.Raw.GetRawText())!,
                    CcVersion = e.CcVersion,
                });
                inserted++;

                session.MessageCount++;
                if (e.Timestamp > session.LastEventAt) session.LastEventAt = e.Timestamp;
                if (e.Timestamp < session.StartedAt) session.StartedAt = e.Timestamp;
                if (!string.IsNullOrWhiteSpace(e.Title)) session.Title = e.Title;
                if (e.CcVersion is not null) session.CcVersion = e.CcVersion;
                if (e.GitBranch is not null) session.GitBranch = e.GitBranch;
            }

            // Any live batch means the session is currently active.
            if (session.Status == SessionStatus.Interrupted || session.Status == SessionStatus.Unknown)
                session.Status = SessionStatus.Live;
        }

        await db.SaveChangesAsync(ct);
        return new IngestResult { Received = batch.Events.Count, Inserted = inserted, Duplicates = duplicates };
    }

    private static string? StripNul(string? s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        if (s.Contains(NulEscape)) s = s.Replace(NulEscape, "");
        if (s.Contains(NulChar)) s = s.Replace(NulChar, "");
        return s;
    }
}
