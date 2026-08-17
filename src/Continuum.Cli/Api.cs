using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Continuum.Cli;

/// <summary>
/// HTTP to the Continuum backend.
///
/// Plain <see cref="HttpClient"/>, not a shell-out to <c>curl.exe</c>. The PowerShell relay used
/// curl because .NET's Invoke-RestMethod hung on a dead IPv6 route to Cloudflare on one machine;
/// the fix for that is to control address selection here, not to depend on a Windows binary.
/// </summary>
public sealed class Api : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly HttpClient _http;

    public Api(Config cfg, TimeSpan? timeout = null)
    {
        // Prefer IPv4. A host whose AAAA route blackholes makes every request hang until timeout,
        // which is what forced the original curl workaround.
        var handler = new SocketsHttpHandler
        {
            ConnectCallback = async (ctx, ct) =>
            {
                var entries = await Dns.GetHostAddressesAsync(ctx.DnsEndPoint.Host, ct);
                var ordered = entries
                    .OrderBy(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 0 : 1)
                    .ToArray();

                Exception? last = null;
                foreach (var addr in ordered)
                {
                    var socket = new System.Net.Sockets.Socket(
                        addr.AddressFamily, System.Net.Sockets.SocketType.Stream, System.Net.Sockets.ProtocolType.Tcp)
                    { NoDelay = true };
                    try
                    {
                        using var attempt = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        attempt.CancelAfter(TimeSpan.FromSeconds(5));
                        await socket.ConnectAsync(addr, ctx.DnsEndPoint.Port, attempt.Token);
                        return new System.Net.Sockets.NetworkStream(socket, ownsSocket: true);
                    }
                    catch (Exception ex)
                    {
                        socket.Dispose();
                        last = ex;
                    }
                }
                throw last ?? new IOException($"Could not connect to {ctx.DnsEndPoint.Host}.");
            },
        };

        _http = new HttpClient(handler)
        {
            BaseAddress = new Uri(cfg.Backend),
            Timeout = timeout ?? TimeSpan.FromSeconds(30),
        };
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cfg.Token);
    }

    public async Task<T?> GetAsync<T>(string path, CancellationToken ct = default)
    {
        var res = await _http.GetAsync(path, ct);
        if (!res.IsSuccessStatusCode) throw new ApiException(path, res.StatusCode);
        return await res.Content.ReadFromJsonAsync<T>(Json, ct);
    }

    public async Task<T?> PostAsync<T>(string path, object body, CancellationToken ct = default)
    {
        var res = await _http.PostAsJsonAsync(path, body, Json, ct);
        if (!res.IsSuccessStatusCode) throw new ApiException(path, res.StatusCode);
        if (res.Content.Headers.ContentLength is 0) return default;
        return await res.Content.ReadFromJsonAsync<T>(Json, ct);
    }

    /// <summary>Fire-and-forget: used where a failure genuinely doesn't matter (membership on join).</summary>
    public async Task TryPostAsync(string path, object body, CancellationToken ct = default)
    {
        try { await PostAsync<object>(path, body, ct); } catch { /* best effort */ }
    }

    public void Dispose() => _http.Dispose();
}

/// <summary>Carries the status code, so callers can tell "not found" from "backend is down".</summary>
public sealed class ApiException(string path, System.Net.HttpStatusCode status)
    : Exception($"{path} → {(int)status} {status}")
{
    public System.Net.HttpStatusCode Status { get; } = status;
}
