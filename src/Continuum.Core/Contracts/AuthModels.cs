using Continuum.Core.Domain;

namespace Continuum.Core.Contracts;

public sealed record LoginRequest(string Email, string Password);
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public sealed record MeDto(Guid Id, string Email, string DisplayName, UserRole Role, bool IsLegacy, bool MustChangePassword);

public sealed record CreatePatRequest(string Name, int? ExpiresDays);
public sealed record PatDto(Guid Id, string Name, string Prefix, DateTimeOffset CreatedAt, DateTimeOffset? LastUsedAt, DateTimeOffset? RevokedAt, DateTimeOffset? ExpiresAt);
/// <summary>Returned once on creation — <see cref="Token"/> is never retrievable again.</summary>
public sealed record PatCreatedDto(Guid Id, string Name, string Token, string Prefix, DateTimeOffset CreatedAt, DateTimeOffset? ExpiresAt);

public sealed record CreateUserRequest(string Email, string DisplayName, string Password, UserRole Role);
public sealed record UpdateUserRequest(bool? Disabled, UserRole? Role);
public sealed record ResetPasswordRequest(string NewPassword);
public sealed record UserDto(Guid Id, string Email, string DisplayName, UserRole Role, bool Disabled, DateTimeOffset CreatedAt, DateTimeOffset? LastLoginAt);

public sealed record ShareRequest(bool Shared);
