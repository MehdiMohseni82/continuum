using Continuum.Core.Access;
using Continuum.Core.Domain;

namespace Continuum.Host.Auth;

/// <summary>The authenticated principal for the current request. Populated by <see cref="AuthFilter"/>.</summary>
public interface ICurrentUser : IAccessPrincipal
{
    string? Email { get; }
    UserRole Role { get; }
    bool IsAuthenticated { get; }
    /// <summary>True when resolved via the legacy shared token rather than a real account/PAT.</summary>
    bool IsLegacy { get; }
}

/// <summary>Scoped, request-lifetime holder the auth filter writes and services read.</summary>
public sealed class CurrentUserAccessor : ICurrentUser
{
    public Guid? UserId { get; private set; }
    public string? Email { get; private set; }
    public UserRole Role { get; private set; } = UserRole.Member;
    public bool IsLegacy { get; private set; }
    public Guid? OrgId { get; private set; }

    public bool IsAuthenticated => UserId is not null;
    public bool IsAdmin => IsAuthenticated && Role == UserRole.Admin;

    /// <param name="orgId">
    /// The organization this request acts in — the caller's membership. Null when they belong to none,
    /// which the access policy reads as "sees nothing" rather than "sees everything".
    /// </param>
    public void Set(User user, bool legacy, Guid? orgId)
    {
        UserId = user.Id;
        Email = user.Email;
        Role = user.Role;
        IsLegacy = legacy;
        OrgId = orgId;
    }
}
