namespace Continuum.Core.Domain;

/// <summary>
/// A tenant. Every workspace, session, memory, agent, channel and room belongs to exactly one, and no
/// query crosses the boundary — not even for an instance administrator, who can operate the server
/// without being able to read what its organizations hold.
/// <para>
/// Pre-tenancy data lives in <see cref="Defaults.DefaultOrgId"/>, so a single-organization instance
/// behaves exactly as it did before organizations existed.
/// </para>
/// </summary>
public class Organization
{
    public Guid Id { get; set; }

    public required string Name { get; set; }

    /// <summary>Short url-safe identifier, unique across the instance.</summary>
    public required string Slug { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public List<OrgMembership> Members { get; } = [];
}

/// <summary>A person's membership of one organization. A user may belong to several.</summary>
public class OrgMembership
{
    public Guid Id { get; set; }

    public Guid OrgId { get; set; }
    public Organization? Organization { get; set; }

    public Guid UserId { get; set; }
    public User? User { get; set; }

    public OrgRole Role { get; set; } = OrgRole.Member;

    public DateTimeOffset JoinedAt { get; set; }
}
