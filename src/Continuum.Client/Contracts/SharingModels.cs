using Continuum.Core.Domain;

namespace Continuum.Core.Contracts;

/// <summary>Share a resource with one user or one team.</summary>
public sealed record GrantRequest(
    GrantResource ResourceType,
    Guid ResourceId,
    GrantPrincipal PrincipalType,
    Guid PrincipalId,
    GrantAccess Access = GrantAccess.Read,
    DateTimeOffset? ExpiresAt = null);

public sealed record GrantDto(
    Guid Id,
    GrantResource ResourceType,
    Guid ResourceId,
    GrantPrincipal PrincipalType,
    Guid PrincipalId,
    GrantAccess Access,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt);

public sealed record TeamDto(Guid Id, string Name, int MemberCount);

public sealed record CreateTeamRequest(string Name);

public sealed record TeamMemberRequest(Guid UserId);
