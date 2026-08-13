namespace Continuum.Core.Domain;

/// <summary>
/// One person or team being given access to one thing. This is what lets sharing name a recipient:
/// before grants, a session was either private or visible to the entire organization, with nothing
/// in between.
/// <para>
/// Grants never cross an organization — <see cref="OrgId"/> is carried so the tenant clause applies
/// to the grant itself, not only to the resource it points at.
/// </para>
/// </summary>
public class Grant
{
    public Guid Id { get; set; }

    /// <summary>The organization the resource and the recipient both belong to.</summary>
    public Guid OrgId { get; set; }

    public GrantResource ResourceType { get; set; }
    public Guid ResourceId { get; set; }

    public GrantPrincipal PrincipalType { get; set; }

    /// <summary>A user id or a team id, per <see cref="PrincipalType"/>.</summary>
    public Guid PrincipalId { get; set; }

    public GrantAccess Access { get; set; } = GrantAccess.Read;

    /// <summary>Who granted it — kept so "who gave them access?" is answerable.</summary>
    public Guid GrantedByUserId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Null never expires. An expired grant stops conferring access without being deleted.</summary>
    public DateTimeOffset? ExpiresAt { get; set; }
}

/// <summary>A named group of people inside one organization, so sharing doesn't mean naming six people each time.</summary>
public class Team
{
    public Guid Id { get; set; }

    public Guid OrgId { get; set; }

    public required string Name { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public List<TeamMember> Members { get; } = [];
}

/// <summary>Someone's membership of a team.</summary>
public class TeamMember
{
    public Guid Id { get; set; }

    public Guid TeamId { get; set; }
    public Team? Team { get; set; }

    public Guid UserId { get; set; }
    public User? User { get; set; }

    public DateTimeOffset JoinedAt { get; set; }
}
