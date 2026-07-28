using Continuum.Core.Contracts;
using Continuum.Core.Domain;
using Continuum.Host.Auth;
using Continuum.Host.Services;

namespace Continuum.Host.Api;

public static class AuthEndpoints
{
    private static readonly TimeSpan CookieLifetime = TimeSpan.FromDays(30);

    /// <summary>Public (unauthenticated) auth endpoints — login only.</summary>
    public static IEndpointRouteBuilder MapPublicAuthApi(this IEndpointRouteBuilder app, AuthOptions authOptions)
    {
        var pub = app.MapGroup("/api").DisableAntiforgery();

        pub.MapPost("/auth/login", async (LoginRequest req, AuthService auth, TokenSigner signer, HttpContext http, CancellationToken ct) =>
        {
            var user = await auth.LoginAsync(req.Email, req.Password, ct);
            if (user is null) return Results.Unauthorized();

            var token = signer.Issue(user.Id, CookieLifetime);
            http.Response.Cookies.Append(AuthFilter.SessionCookie, token, CookieOptions(authOptions));
            return Results.Ok(new MeDto(user.Id, user.Email, user.DisplayName, user.Role, false, user.MustChangePassword));
        });

        return app;
    }

    /// <summary>Authenticated auth + account-management endpoints (behind AuthFilter).</summary>
    public static void MapAuthApi(this RouteGroupBuilder api, AuthOptions authOptions)
    {
        api.MapPost("/auth/logout", (HttpContext http) =>
        {
            http.Response.Cookies.Delete(AuthFilter.SessionCookie);
            return Results.NoContent();
        });

        api.MapGet("/auth/me", async (ICurrentUser me, AuthService auth, CancellationToken ct) =>
        {
            if (me.UserId is null) return Results.Unauthorized();
            var user = me.IsLegacy ? null : await auth.FindByIdAsync(me.UserId.Value, ct);
            var name = user?.DisplayName ?? "Admin (legacy token)";
            var email = user?.Email ?? me.Email ?? "";
            return Results.Ok(new MeDto(me.UserId.Value, email, name, me.Role, me.IsLegacy, user?.MustChangePassword ?? false));
        });

        api.MapPost("/auth/password", async (ChangePasswordRequest req, ICurrentUser me, AuthService auth, CancellationToken ct) =>
        {
            if (me.UserId is null || me.IsLegacy) return Results.Unauthorized();
            var user = await auth.FindByIdAsync(me.UserId.Value, ct);
            if (user is null || !PasswordHasher.Verify(req.CurrentPassword, user.PasswordHash)) return Results.Unauthorized();
            if (req.NewPassword.Length < 8) return Results.BadRequest("Password must be at least 8 characters.");
            await auth.SetPasswordAsync(user.Id, req.NewPassword, mustChange: false, ct);
            return Results.NoContent();
        });

        // ---- personal access tokens (self) ----
        api.MapGet("/auth/tokens", async (ICurrentUser me, AuthService auth, CancellationToken ct) =>
        {
            if (me.UserId is null || me.IsLegacy) return Results.Ok(Array.Empty<PatDto>());
            var tokens = await auth.ListPatsAsync(me.UserId.Value, ct);
            return Results.Ok(tokens.Select(ToPatDto));
        });

        api.MapPost("/auth/tokens", async (CreatePatRequest req, ICurrentUser me, AuthService auth, CancellationToken ct) =>
        {
            if (me.UserId is null || me.IsLegacy)
                return Results.BadRequest("Log in with a real account (not the legacy token) to create a personal access token.");
            var name = string.IsNullOrWhiteSpace(req.Name) ? "token" : req.Name.Trim();
            DateTimeOffset? expires = req.ExpiresDays is > 0 ? DateTimeOffset.UtcNow.AddDays(req.ExpiresDays.Value) : null;
            var issued = await auth.CreatePatAsync(me.UserId.Value, name, expires, ct);
            return Results.Ok(new PatCreatedDto(issued.Id, issued.Name, issued.Token, issued.Prefix, issued.CreatedAt, issued.ExpiresAt));
        });

        api.MapDelete("/auth/tokens/{id:guid}", async (Guid id, ICurrentUser me, AuthService auth, CancellationToken ct) =>
        {
            if (me.UserId is null) return Results.Unauthorized();
            return await auth.RevokePatAsync(me.UserId.Value, id, ct) ? Results.NoContent() : Results.NotFound();
        });

        // ---- user management (admin only) ----
        api.MapGet("/users", async (ICurrentUser me, AuthService auth, CancellationToken ct) =>
        {
            if (!me.IsAdmin) return Results.Forbid();
            var users = await auth.ListUsersAsync(ct);
            return Results.Ok(users.Select(ToUserDto));
        });

        api.MapPost("/users", async (CreateUserRequest req, ICurrentUser me, AuthService auth, CancellationToken ct) =>
        {
            if (!me.IsAdmin) return Results.Forbid();
            if (string.IsNullOrWhiteSpace(req.Email) || req.Password.Length < 8)
                return Results.BadRequest("Email required and password must be at least 8 characters.");
            if (await auth.FindByEmailAsync(req.Email, ct) is not null)
                return Results.Conflict("A user with that email already exists.");
            var user = await auth.CreateUserAsync(req.Email, req.DisplayName, req.Password, req.Role, mustChange: true, ct);
            return Results.Ok(ToUserDto(user));
        });

        api.MapPatch("/users/{id:guid}", async (Guid id, UpdateUserRequest req, ICurrentUser me, AuthService auth, CancellationToken ct) =>
        {
            if (!me.IsAdmin) return Results.Forbid();
            if (id == Defaults.DefaultOwnerId && req.Disabled == true)
                return Results.BadRequest("The bootstrap admin cannot be disabled.");
            var user = await auth.FindByIdAsync(id, ct);
            if (user is null) return Results.NotFound();
            if (req.Disabled is { } d) await auth.SetDisabledAsync(id, d, ct);
            if (req.Role is { } r) await auth.SetRoleAsync(id, r, ct);
            return Results.Ok(ToUserDto((await auth.FindByIdAsync(id, ct))!));
        });

        api.MapPost("/users/{id:guid}/reset-password", async (Guid id, ResetPasswordRequest req, ICurrentUser me, AuthService auth, CancellationToken ct) =>
        {
            if (!me.IsAdmin) return Results.Forbid();
            if (req.NewPassword.Length < 8) return Results.BadRequest("Password must be at least 8 characters.");
            return await auth.SetPasswordAsync(id, req.NewPassword, mustChange: true, ct) ? Results.NoContent() : Results.NotFound();
        });
    }

    private static CookieOptions CookieOptions(AuthOptions o) => new()
    {
        HttpOnly = true,
        Secure = o.SecureCookie,
        SameSite = SameSiteMode.Lax,
        Path = "/",
        MaxAge = CookieLifetime,
    };

    private static PatDto ToPatDto(AccessToken t) =>
        new(t.Id, t.Name, t.Prefix, t.CreatedAt, t.LastUsedAt, t.RevokedAt, t.ExpiresAt);

    private static UserDto ToUserDto(User u) =>
        new(u.Id, u.Email, u.DisplayName, u.Role, u.Disabled, u.CreatedAt, u.LastLoginAt);
}
