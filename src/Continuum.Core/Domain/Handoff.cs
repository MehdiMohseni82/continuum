namespace Continuum.Core.Domain;

/// <summary>
/// A task one agent packages for another to pick up — e.g. a research session hands an
/// implementation task to an implementer session, carrying a pointer to the relevant context.
/// </summary>
public class Handoff
{
    public Guid Id { get; set; }

    public Guid FromAgentId { get; set; }
    public Agent? FromAgent { get; set; }

    public Guid? ClaimedByAgentId { get; set; }
    public Agent? ClaimedByAgent { get; set; }

    public Guid? WorkspaceId { get; set; }

    public required string Title { get; set; }

    /// <summary>The task packet — what needs doing.</summary>
    public required string Task { get; set; }

    /// <summary>Pointer to context: a session id, checkpoint id, or free text.</summary>
    public string? ContextRef { get; set; }

    /// <summary>open | claimed | done</summary>
    public string Status { get; set; } = "open";

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ClaimedAt { get; set; }
}
