namespace Continuum.Core.Domain;

/// <summary>
/// A person's standing inside one organization. Distinct from <see cref="UserRole"/>, which is about
/// operating the instance: someone can administer their own organization without being able to touch
/// anyone else's, and an instance administrator is not automatically a member of every organization.
/// </summary>
public enum OrgRole
{
    /// <summary>Works in the organization; sees what they own and what has been shared with them.</summary>
    Member = 0,
    /// <summary>Manages membership and organization settings.</summary>
    Admin = 1,
    /// <summary>Created the organization. Cannot be removed by another member.</summary>
    Owner = 2,
}
