namespace Continuum.Core.Domain;

/// <summary>What a <see cref="Grant"/> is about.</summary>
public enum GrantResource
{
    Session = 0,
    Memory = 1,
    Room = 2,
}

/// <summary>
/// Who a grant is made to. There is deliberately no "organization" member: sharing with the whole
/// organization is what the <c>Shared</c> flag already means, and having two ways to express the same
/// thing is how the two drift apart. Grants exist for the case a flag can't express — naming people.
/// </summary>
public enum GrantPrincipal
{
    User = 0,
    Team = 1,
}

/// <summary>How much a grant confers.</summary>
public enum GrantAccess
{
    /// <summary>May read the resource.</summary>
    Read = 0,

    /// <summary>May read and take part — post into a room, add to a shared workspace.</summary>
    Contribute = 1,
}
