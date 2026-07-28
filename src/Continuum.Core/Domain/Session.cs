namespace Continuum.Core.Domain;

/// <summary>
/// One Claude Code session. The primary key is the source session id from the JSONL,
/// which is a globally-unique GUID — so ingest is naturally idempotent across machines.
/// Aggregate fields (counts, timestamps, title) are recomputed from events as they arrive.
/// </summary>
public class Session
{
    /// <summary>Source session id (from the JSONL / filename).</summary>
    public Guid Id { get; set; }

    public Guid MachineId { get; set; }
    public Machine? Machine { get; set; }

    public Guid WorkspaceId { get; set; }
    public Workspace? Workspace { get; set; }

    public string? Title { get; set; }
    public string? Summary { get; set; }
    public string? GitBranch { get; set; }
    public string? CcVersion { get; set; }

    public SessionStatus Status { get; set; } = SessionStatus.Live;

    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset LastEventAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }

    public int MessageCount { get; set; }

    public List<Event> Events { get; } = [];
}
