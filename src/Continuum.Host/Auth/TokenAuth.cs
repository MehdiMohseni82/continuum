using Microsoft.Net.Http.Headers;

namespace Continuum.Host.Auth;

/// <summary>
/// Single-user auth: a shared bearer token. Configured via <c>Continuum:Token</c>
/// (env var <c>CONTINUUM_TOKEN</c>). Applied to API routes as an endpoint filter.
/// </summary>
public sealed class BearerTokenFilter(IConfiguration config) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        var expected = config["Continuum:Token"];
        if (string.IsNullOrEmpty(expected))
            return Results.Problem("Server has no CONTINUUM_TOKEN configured.", statusCode: StatusCodes.Status500InternalServerError);

        var header = ctx.HttpContext.Request.Headers[HeaderNames.Authorization].ToString();
        const string prefix = "Bearer ";
        var presented = header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? header[prefix.Length..].Trim()
            : null;

        if (presented is null || !FixedTimeEquals(presented, expected))
            return Results.Unauthorized();

        return await next(ctx);
    }

    private static bool FixedTimeEquals(string a, string b)
    {
        var ba = System.Text.Encoding.UTF8.GetBytes(a);
        var bb = System.Text.Encoding.UTF8.GetBytes(b);
        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(ba, bb);
    }
}
