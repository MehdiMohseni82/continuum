using System.Net.Http.Headers;
using System.Net.Http.Json;
using Continuum.Core.Contracts;

namespace Continuum.Daemon;

/// <summary>Posts batches to the backend ingest API.</summary>
public sealed class BackendClient(HttpClient http)
{
    public async Task<IngestResult?> UploadAsync(IngestBatch batch, CancellationToken ct)
    {
        using var resp = await http.PostAsJsonAsync("/api/ingest/batch", batch, ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<IngestResult>(ct);
    }

    public static void Configure(HttpClient http, DaemonOptions options)
    {
        http.BaseAddress = new Uri(options.BackendUrl);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", options.Token);
        http.Timeout = TimeSpan.FromMinutes(2);
    }
}
