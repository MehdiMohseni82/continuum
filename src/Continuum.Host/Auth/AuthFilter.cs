using Continuum.Core.Domain;
using Continuum.Host.Services;
using Microsoft.Net.Http.Headers;

namespace Continuum.Host.Auth;

public sealed class AuthOptions
{
    /// <summary>The legacy shared token still resolves to the bootstrap admin while true (rollout fallback).</summary>
    public bool AllowLegacyToken { get; set; } = true;

    /// <summary>Set the Secure flag on the session cookie (true in prod/HTTPS; false for local http testing).</summary>
    public bool SecureCookie { get; set; } = true;
}

/// <summary>
/// Resolves the request principal and fills <see cref="CurrentUserAccessor"/>. Order:
/// session cookie → Bearer PAT → legacy shared token (→ bootstrap admin, if still allowed).
/// Returns 401 when none resolve.
/// </summary>
public sealed class AuthFilter(
    IConfiguration config,
    AuthOptions options,
    TokenSigner signer,
    AuthService auth,
    CurrentUserAccessor current) : IEndpointFilter
{
    public const string SessionCookie = "continuum_session";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        var http = ctx.HttpContext;
        var ok = await ResolveCookieAsync(http, current)
                 || await ResolveBearerAsync(http, current);

        if (!ok) return Results.Unauthorized();
        return await next(ctx);
    }

    private async Task<bool> ResolveCookieAsync(HttpContext http, CurrentUserAccessor cur)
    {
        if (!http.Request.Cookies.TryGetValue(SessionCookie, out var cookie)) return false;
        var userId = signer.Validate(cookie);
        if (userId is null) return false;
        var user = await auth.FindByIdAsync(userId.Value, http.RequestAborted);
        if (user is null || user.Disabled) return false;
        await SetAsync(cur, user, legacy: false, http.RequestAborted);
        return true;
    }

    /// <summary>Resolve which organization the request acts in, then fill the principal.</summary>
    private async Task SetAsync(CurrentUserAccessor cur, User user, bool legacy, CancellationToken ct) =>
        cur.Set(user, legacy, await auth.PrimaryOrgIdAsync(user.Id, ct));

    private async Task<bool> ResolveBearerAsync(HttpContext http, CurrentUserAccessor cur)
    {
        var header = http.Request.Headers[HeaderNames.Authorization].ToString();
        const string prefix = "Bearer ";
        if (!header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
        var presented = header[prefix.Length..].Trim();
        if (presented.Length == 0) return false;

        // Legacy shared token → bootstrap admin (rollout fallback; the user disables this later).
        var legacy = config["Continuum:Token"];
        if (options.AllowLegacyToken && !string.IsNullOrEmpty(legacy)
            && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.UTF8.GetBytes(presented), System.Text.Encoding.UTF8.GetBytes(legacy)))
        {
            var admin = await auth.FindByIdAsync(Defaults.DefaultOwnerId, http.RequestAborted)
                        ?? new User { Id = Defaults.DefaultOwnerId, Email = "admin@continuum.local", DisplayName = "Admin", PasswordHash = "", Role = UserRole.Admin };
            // The legacy token predates organizations, so it acts in the one that holds pre-tenancy data.
            cur.Set(admin, legacy: true, await auth.PrimaryOrgIdAsync(admin.Id, http.RequestAborted) ?? Defaults.DefaultOrgId);
            return true;
        }

        var user = await auth.ResolvePatAsync(presented, http.RequestAborted);
        if (user is null) return false;
        await SetAsync(cur, user, legacy: false, http.RequestAborted);
        return true;
    }
}
