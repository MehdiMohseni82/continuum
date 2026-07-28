namespace Continuum.Core.Domain;

/// <summary>
/// A curated snapshot of a session's working context — open threads, decisions, next steps —
/// captured before the context window compacts (or on demand) so key state survives.
/// </summary>
public class Checkpoint
{
    public Guid Id { get; set; }

    public Guid SessionId { get; set; }
    public Session? Session { get; set; }

    public Guid? WorkspaceId { get; set; }

    /// <summary>Markdown snapshot of the working context.</summary>
    public required string Content { get; set; }

    /// <summary>Why it was taken: pre-compact | manual | stop.</summary>
    public required string Reason { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
