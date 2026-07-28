namespace Continuum.Core.Domain;

public enum UserRole
{
    /// <summary>Sees only their own (and shared) data; manages their own tokens.</summary>
    Member = 0,
    /// <summary>Sees all data and manages users.</summary>
    Admin = 1,
}

/// <summary>
/// A person with a login. The bootstrap admin is seeded with <see cref="Defaults.DefaultOwnerId"/>
/// so all pre-accounts data (which carried that owner id) belongs to them with no data migration.
/// </summary>
public class User
{
    public Guid Id { get; set; }

    /// <summary>Lower-cased, unique. The login identifier.</summary>
    public required string Email { get; set; }

    public required string DisplayName { get; set; }

    /// <summary>PBKDF2 hash string (iterations.salt.key, all base64). Never the raw password.</summary>
    public required string PasswordHash { get; set; }

    public UserRole Role { get; set; } = UserRole.Member;

    /// <summary>A disabled user cannot log in and their tokens stop working.</summary>
    public bool Disabled { get; set; }

    /// <summary>Force a password change on next login (e.g. after an admin reset).</summary>
    public bool MustChangePassword { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }

    public List<AccessToken> Tokens { get; } = [];
}

/// <summary>
/// A revocable Personal Access Token for non-interactive clients (daemon, MCP, API). The raw token
/// is shown once at creation; only its hash is stored. <see cref="Prefix"/> is a non-secret label.
/// </summary>
public class AccessToken
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }
    public User? User { get; set; }

    /// <summary>Human label, e.g. the machine it lives on ("desktop", "laptop").</summary>
    public required string Name { get; set; }

    /// <summary>SHA-256 of the raw token (hex). Lookups hash-then-match.</summary>
    public required string TokenHash { get; set; }

    /// <summary>First few non-secret chars for display, e.g. "cnt_a1b2c3".</summary>
    public required string Prefix { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastUsedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }

    public bool IsActive(DateTimeOffset now) => RevokedAt is null && (ExpiresAt is null || ExpiresAt > now);
}
