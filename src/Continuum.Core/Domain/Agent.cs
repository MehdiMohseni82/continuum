namespace Continuum.Core.Domain;

/// <summary>A participant on the bus — a Claude session that registered an identity so others can reach it.</summary>
public class Agent
{
    public Guid Id { get; set; }

    /// <summary>Human-chosen handle, unique per owner (e.g. "researcher", "implementer").</summary>
    public required string Name { get; set; }

    /// <summary>The organization this belongs to. No query crosses organizations.</summary>
    public Guid OrgId { get; set; } = Defaults.DefaultOrgId;

    public Guid OwnerId { get; set; } = Defaults.DefaultOwnerId;

    public string? MachineName { get; set; }
    public Guid? CurrentSessionId { get; set; }

    /// <summary>Free-text capabilities so peers know what this agent can do.</summary>
    public string? Capabilities { get; set; }

    public DateTimeOffset RegisteredAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
}
