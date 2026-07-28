namespace Continuum.Core.Domain;

/// <summary>
/// A project / repo, keyed by the Claude Code project directory name (the "D--..." hash).
/// Phase 0 keeps one workspace per project key; Phase 1 lets one workspace own several keys
/// so the same repo on different machines collapses into a single project.
/// </summary>
public class Workspace
{
    public Guid Id { get; set; }

    /// <summary>The project directory name under ~/.claude/projects. Unique.</summary>
    public required string ProjectKey { get; set; }

    /// <summary>Friendly name shown in the UI; defaults to the project key.</summary>
    public required string DisplayName { get; set; }

    /// <summary>Team-ready seam: every workspace has an owner. Single-user resolves to a default id.</summary>
    public Guid OwnerId { get; set; } = Defaults.DefaultOwnerId;

    public DateTimeOffset FirstSeenAt { get; set; }

    public List<Session> Sessions { get; } = [];
}
