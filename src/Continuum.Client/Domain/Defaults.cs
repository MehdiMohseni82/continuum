namespace Continuum.Core.Domain;

/// <summary>Well-known constants for the single-user phase.</summary>
public static class Defaults
{
    /// <summary>The stand-in owner id used until real accounts exist (team-ready seam).</summary>
    public static readonly Guid DefaultOwnerId = new("00000000-0000-0000-0000-000000000001");

    /// <summary>
    /// The organization every pre-tenancy row belongs to. Fixed rather than generated so the migration
    /// that introduces organizations can backfill existing rows with a column default and no data
    /// migration step — exactly the trick <see cref="DefaultOwnerId"/> played for accounts.
    /// </summary>
    public static readonly Guid DefaultOrgId = new("00000000-0000-0000-0000-000000000002");
}
