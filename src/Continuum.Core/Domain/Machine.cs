namespace Continuum.Core.Domain;

/// <summary>A computer that a daemon runs on and streams sessions from.</summary>
public class Machine
{
    public Guid Id { get; set; }

    /// <summary>Stable, human-friendly name (e.g. "desktop", "laptop"). Unique.</summary>
    public required string Name { get; set; }

    public DateTimeOffset? LastSeenAt { get; set; }

    public List<Session> Sessions { get; } = [];
}
