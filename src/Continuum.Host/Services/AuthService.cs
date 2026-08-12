using System.Security.Cryptography;
using Continuum.Core.Data;
using Continuum.Core.Domain;
using Continuum.Host.Auth;
using Microsoft.EntityFrameworkCore;

namespace Continuum.Host.Services;

public sealed record PatIssued(Guid Id, string Name, string Token, string Prefix, DateTimeOffset CreatedAt, DateTimeOffset? ExpiresAt);

/// <summary>Accounts, passwords, and Personal Access Tokens. All auth reads/writes funnel through here.</summary>
public sealed class AuthService(ContinuumDbContext db)
{
    public const string TokenPrefix = "cnt_";

    // ---- accounts ----

    public Task<User?> FindByEmailAsync(string email, CancellationToken ct) =>
        db.Users.FirstOrDefaultAsync(u => u.Email == Normalize(email), ct);

    public Task<User?> FindByIdAsync(Guid id, CancellationToken ct) =>
        db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);

    /// <summary>
    /// The organization a request by this user acts in. A user may belong to several; until there is a
    /// way to choose (an explicit switcher in the UI, or a header for tools), the oldest membership
    /// wins, which is stable and matches the single-organization case exactly. Null when they belong
    /// to none — the access policy reads that as seeing nothing.
    /// </summary>
    public Task<Guid?> PrimaryOrgIdAsync(Guid userId, CancellationToken ct) =>
        db.OrgMemberships
            .Where(m => m.UserId == userId)
            .OrderBy(m => m.JoinedAt).ThenBy(m => m.Id)
            .Select(m => (Guid?)m.OrgId)
            .FirstOrDefaultAsync(ct);

    /// <summary>Validate credentials. Returns the user on success, null on any failure (unknown, wrong, disabled).</summary>
    public async Task<User?> LoginAsync(string email, string password, CancellationToken ct)
    {
        var user = await FindByEmailAsync(email, ct);
        if (user is null || user.Disabled || !PasswordHasher.Verify(password, user.PasswordHash)) return null;
        user.LastLoginAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return user;
    }

    public async Task<User> CreateUserAsync(string email, string displayName, string password, UserRole role, bool mustChange, CancellationToken ct)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = Normalize(email),
            DisplayName = displayName,
            PasswordHash = PasswordHasher.Hash(password),
            Role = role,
            MustChangePassword = mustChange,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Users.Add(user);

        // A user with no membership sees nothing, so enrol them as they're created. Phase 2 has one
        // organization; choosing which one (an invite, or the creating admin's) comes with orgs in
        // the UI.
        db.OrgMemberships.Add(new OrgMembership
        {
            Id = Guid.NewGuid(),
            OrgId = Defaults.DefaultOrgId,
            UserId = user.Id,
            Role = role == UserRole.Admin ? OrgRole.Admin : OrgRole.Member,
            JoinedAt = DateTimeOffset.UtcNow,
        });

        await db.SaveChangesAsync(ct);
        return user;
    }

    public Task<List<User>> ListUsersAsync(CancellationToken ct) =>
        db.Users.OrderBy(u => u.CreatedAt).ToListAsync(ct);

    public async Task<bool> SetDisabledAsync(Guid userId, bool disabled, CancellationToken ct)
    {
        var user = await FindByIdAsync(userId, ct);
        if (user is null) return false;
        user.Disabled = disabled;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> SetRoleAsync(Guid userId, UserRole role, CancellationToken ct)
    {
        var user = await FindByIdAsync(userId, ct);
        if (user is null) return false;
        user.Role = role;
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>Set a new password. Used by both admin reset (mustChange=true) and self-service change.</summary>
    public async Task<bool> SetPasswordAsync(Guid userId, string newPassword, bool mustChange, CancellationToken ct)
    {
        var user = await FindByIdAsync(userId, ct);
        if (user is null) return false;
        user.PasswordHash = PasswordHasher.Hash(newPassword);
        user.MustChangePassword = mustChange;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public Task<int> UserCountAsync(CancellationToken ct) => db.Users.CountAsync(ct);

    // ---- personal access tokens ----

    public async Task<PatIssued> CreatePatAsync(Guid userId, string name, DateTimeOffset? expiresAt, CancellationToken ct)
    {
        var raw = TokenPrefix + Base64Url(RandomNumberGenerator.GetBytes(32));
        var token = new AccessToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = name,
            TokenHash = HashToken(raw),
            Prefix = raw[..Math.Min(12, raw.Length)],
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = expiresAt,
        };
        db.AccessTokens.Add(token);
        await db.SaveChangesAsync(ct);
        return new PatIssued(token.Id, token.Name, raw, token.Prefix, token.CreatedAt, token.ExpiresAt);
    }

    public Task<List<AccessToken>> ListPatsAsync(Guid userId, CancellationToken ct) =>
        db.AccessTokens.Where(t => t.UserId == userId).OrderByDescending(t => t.CreatedAt).ToListAsync(ct);

    public async Task<bool> RevokePatAsync(Guid userId, Guid tokenId, CancellationToken ct)
    {
        var token = await db.AccessTokens.FirstOrDefaultAsync(t => t.Id == tokenId && t.UserId == userId, ct);
        if (token is null || token.RevokedAt is not null) return false;
        token.RevokedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>Resolve a raw PAT to its (non-disabled) user, touching LastUsedAt. Null if invalid.</summary>
    public async Task<User?> ResolvePatAsync(string rawToken, CancellationToken ct)
    {
        var hash = HashToken(rawToken);
        var token = await db.AccessTokens.Include(t => t.User).FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (token?.User is null || token.User.Disabled || !token.IsActive(DateTimeOffset.UtcNow)) return null;
        token.LastUsedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return token.User;
    }

    public static string HashToken(string raw) =>
        Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();

    private static string Normalize(string email) => email.Trim().ToLowerInvariant();

    private static string Base64Url(byte[] b) =>
        Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
