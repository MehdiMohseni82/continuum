using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Continuum.Core.Contracts;
using Continuum.Core.Domain;

namespace Continuum.Mcp;

/// <summary>
/// Thin HTTP client the MCP tools use to reach the Continuum backend. Keeping the MCP server a
/// pure client (no direct DB access) means the backend stays the single source of truth.
/// Configured from env: CONTINUUM_BACKEND (default http://localhost:5000), CONTINUUM_TOKEN.
/// </summary>
public sealed class BackendApi
{
    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private readonly HttpClient _http;
    private Dictionary<string, Guid>? _workspaceCache;

    public BackendApi()
    {
        var baseUrl = Environment.GetEnvironmentVariable("CONTINUUM_BACKEND") ?? "http://localhost:5000";
        var token = Environment.GetEnvironmentVariable("CONTINUUM_TOKEN") ?? "dev-local-token-change-me";
        _http = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(30) };
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    public async Task<MemoryDto> SaveMemoryAsync(string content, MemoryType type, string? projectKey, Guid? sessionId, bool pinned, CancellationToken ct)
    {
        var req = new MemorySaveRequest
        {
            Type = type,
            Content = content,
            WorkspaceId = await ResolveWorkspaceAsync(projectKey, ct),
            SourceSessionId = sessionId,
            Pinned = pinned,
        };
        var resp = await _http.PostAsJsonAsync("/api/memory", req, Json, ct);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<MemoryDto>(Json, ct))!;
    }

    public async Task<IReadOnlyList<MemoryDto>> SearchMemoryAsync(string query, string? projectKey, int limit, CancellationToken ct)
    {
        var wid = await ResolveWorkspaceAsync(projectKey, ct);
        var url = $"/api/memory/search?q={Uri.EscapeDataString(query)}&take={limit}" + (wid is { } w ? $"&workspaceId={w}" : "");
        return await _http.GetFromJsonAsync<List<MemoryDto>>(url, Json, ct) ?? [];
    }

    public async Task<IReadOnlyList<MemoryDto>> ListMemoryAsync(string? projectKey, int limit, CancellationToken ct)
    {
        var wid = await ResolveWorkspaceAsync(projectKey, ct);
        var url = $"/api/memory?take={limit}" + (wid is { } w ? $"&workspaceId={w}" : "");
        return await _http.GetFromJsonAsync<List<MemoryDto>>(url, Json, ct) ?? [];
    }

    public async Task<bool> ForgetMemoryAsync(Guid id, CancellationToken ct)
    {
        var resp = await _http.DeleteAsync($"/api/memory/{id}", ct);
        return resp.IsSuccessStatusCode;
    }

    public async Task<CheckpointDto> CheckpointAsync(Guid sessionId, string content, string reason, CancellationToken ct)
    {
        var resp = await _http.PostAsJsonAsync("/api/checkpoints", new CheckpointRequest(sessionId, content, reason), Json, ct);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<CheckpointDto>(Json, ct))!;
    }

    public async Task<IReadOnlyList<SearchHitDto>> SearchHistoryAsync(string query, int limit, CancellationToken ct) =>
        await _http.GetFromJsonAsync<List<SearchHitDto>>($"/api/search?q={Uri.EscapeDataString(query)}&take={limit}", Json, ct) ?? [];

    // ---- inter-agent bus ----

    public async Task<AgentDto> RegisterAgentAsync(string name, string? machine, string? caps, CancellationToken ct)
    {
        var resp = await _http.PostAsJsonAsync("/api/agents/register",
            new RegisterAgentRequest(name, machine, null, caps), Json, ct);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<AgentDto>(Json, ct))!;
    }

    public async Task<IReadOnlyList<AgentDto>> ListAgentsAsync(CancellationToken ct) =>
        await _http.GetFromJsonAsync<List<AgentDto>>("/api/agents", Json, ct) ?? [];

    public async Task<MessageDto> SendDirectAsync(string from, string to, string body, CancellationToken ct)
    {
        var resp = await _http.PostAsJsonAsync("/api/bus/send", new SendMessageRequest(from, to, body), Json, ct);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<MessageDto>(Json, ct))!;
    }

    public async Task<IReadOnlyList<MessageDto>> InboxAsync(string agent, bool unreadOnly, CancellationToken ct) =>
        await _http.GetFromJsonAsync<List<MessageDto>>(
            $"/api/bus/inbox?agent={Uri.EscapeDataString(agent)}&unreadOnly={unreadOnly}&markRead=true", Json, ct) ?? [];

    public async Task<MessageDto> PostChannelAsync(string from, string channel, string body, CancellationToken ct)
    {
        var resp = await _http.PostAsJsonAsync("/api/bus/channel", new ChannelPostRequest(from, channel, body), Json, ct);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<MessageDto>(Json, ct))!;
    }

    public async Task<IReadOnlyList<MessageDto>> ReadChannelAsync(string channel, long since, int take, CancellationToken ct) =>
        await _http.GetFromJsonAsync<List<MessageDto>>(
            $"/api/bus/channel?channel={Uri.EscapeDataString(channel)}&since={since}&take={take}", Json, ct) ?? [];

    public async Task<HandoffDto> CreateHandoffAsync(string from, string title, string task, string? contextRef, CancellationToken ct)
    {
        var resp = await _http.PostAsJsonAsync("/api/handoffs",
            new HandoffRequest(from, title, task, contextRef, null), Json, ct);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<HandoffDto>(Json, ct))!;
    }

    public async Task<HandoffDto?> ClaimHandoffAsync(Guid id, string byAgent, CancellationToken ct)
    {
        var resp = await _http.PostAsJsonAsync($"/api/handoffs/{id}/claim", new ClaimHandoffRequest(byAgent), Json, ct);
        return resp.IsSuccessStatusCode ? await resp.Content.ReadFromJsonAsync<HandoffDto>(Json, ct) : null;
    }

    public async Task<IReadOnlyList<HandoffDto>> ListHandoffsAsync(string? status, CancellationToken ct) =>
        await _http.GetFromJsonAsync<List<HandoffDto>>(
            "/api/handoffs" + (status is null ? "" : $"?status={status}"), Json, ct) ?? [];

    private async Task<Guid?> ResolveWorkspaceAsync(string? projectKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(projectKey)) return null;

        _workspaceCache ??= (await _http.GetFromJsonAsync<List<WorkspaceDto>>("/api/workspaces", Json, ct) ?? [])
            .ToDictionary(w => w.ProjectKey, w => w.Id);

        return _workspaceCache.TryGetValue(projectKey, out var id) ? id : null;
    }
}
