namespace Continuum.Core.Domain;

public enum UserRole
{
    /// <summary>Sees only their own (and shared) data; manages their own tokens.</summary>
    Member = 0,
    /// <summary>Sees all data and manages users.</summary>
    Admin = 1,
}
