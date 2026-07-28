using System.Security.Cryptography;
using System.Text;

namespace Continuum.Host.Auth;

/// <summary>
/// Signs short session payloads with HMAC-SHA256 using a server secret. Stable across restarts
/// (the key is config, not an in-memory key ring), so redeploys don't log everyone out.
/// Format: <c>base64url(payload).base64url(hmac)</c> where payload is <c>{userId}:{expiryUnix}</c>.
/// </summary>
public sealed class TokenSigner
{
    private readonly byte[] _key;

    public TokenSigner(IConfiguration config)
    {
        // Dedicated secret if provided, else derive from the existing token so there's always a key.
        var secret = config["Continuum:AuthSecret"];
        if (string.IsNullOrWhiteSpace(secret)) secret = config["Continuum:Token"];
        if (string.IsNullOrWhiteSpace(secret)) secret = "continuum-dev-auth-secret";
        _key = SHA256.HashData(Encoding.UTF8.GetBytes("continuum-session:" + secret));
    }

    public string Issue(Guid userId, TimeSpan lifetime)
    {
        var expiry = DateTimeOffset.UtcNow.Add(lifetime).ToUnixTimeSeconds();
        var payload = $"{userId:N}:{expiry}";
        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        var sig = HMACSHA256.HashData(_key, payloadBytes);
        return $"{Base64Url(payloadBytes)}.{Base64Url(sig)}";
    }

    /// <summary>Returns the userId if the token is well-formed, unexpired, and correctly signed.</summary>
    public Guid? Validate(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        var dot = token.IndexOf('.');
        if (dot <= 0 || dot == token.Length - 1) return null;

        byte[] payloadBytes, sig;
        try
        {
            payloadBytes = FromBase64Url(token[..dot]);
            sig = FromBase64Url(token[(dot + 1)..]);
        }
        catch (FormatException) { return null; }

        var expected = HMACSHA256.HashData(_key, payloadBytes);
        if (!CryptographicOperations.FixedTimeEquals(sig, expected)) return null;

        var parts = Encoding.UTF8.GetString(payloadBytes).Split(':');
        if (parts.Length != 2 || !Guid.TryParseExact(parts[0], "N", out var userId)
            || !long.TryParse(parts[1], out var expiry)) return null;
        if (DateTimeOffset.FromUnixTimeSeconds(expiry) <= DateTimeOffset.UtcNow) return null;

        return userId;
    }

    private static string Base64Url(byte[] b) =>
        Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string s)
    {
        var padded = s.Replace('-', '+').Replace('_', '/');
        padded = (padded.Length % 4) switch { 2 => padded + "==", 3 => padded + "=", _ => padded };
        return Convert.FromBase64String(padded);
    }
}
