namespace Continuum.Core.Domain;

/// <summary>Well-known constants for the single-user phase.</summary>
public static class Defaults
{
    /// <summary>The stand-in owner id used until real accounts exist (team-ready seam).</summary>
    public static readonly Guid DefaultOwnerId = new("00000000-0000-0000-0000-000000000001");
}
