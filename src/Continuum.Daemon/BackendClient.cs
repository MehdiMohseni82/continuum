using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Continuum.Core.Contracts;

namespace Continuum.Daemon;

/// <summary>Posts batches to the backend ingest API, and reads rooms for the room runner.</summary>
public sealed class BackendClient(HttpClient http)
{
    // Room DTOs carry string enums (LanguageMode); the default web serializer needs the converter.
    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public async Task<IngestResult?> UploadAsync(IngestBatch batch, CancellationToken ct)
    {
        using var resp = await http.PostAsJsonAsync("/api/ingest/batch", batch, ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<IngestResult>(ct);
    }

    public async Task<IReadOnlyList<RoomDto>> GetRoomsAsync(CancellationToken ct) =>
        await http.GetFromJsonAsync<List<RoomDto>>("/api/rooms", Json, ct) ?? [];

    public async Task<RoomDetailDto?> GetRoomAsync(Guid id, CancellationToken ct) =>
        await http.GetFromJsonAsync<RoomDetailDto>($"/api/rooms/{id}", Json, ct);

    /// <summary>Best-effort room close (idempotent server-side). Returns false on any non-success — the
    /// caller still stops driving the room, so a failed close never leaves it running.</summary>
    public async Task<bool> CloseRoomAsync(Guid id, CancellationToken ct)
    {
        using var resp = await http.PostAsync($"/api/rooms/{id}/close", content: null, ct);
        return resp.IsSuccessStatusCode;
    }

    public static void Configure(HttpClient http, DaemonOptions options)
    {
        http.BaseAddress = new Uri(options.BackendUrl);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", options.Token);
        http.Timeout = TimeSpan.FromMinutes(2);
    }
}
