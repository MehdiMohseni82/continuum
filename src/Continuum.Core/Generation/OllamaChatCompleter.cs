using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Continuum.Core.Generation;

public sealed class GenerationOptions
{
    /// <summary>Ollama base URL (same server as embeddings).</summary>
    public string Endpoint { get; set; } = "http://localhost:11434";

    /// <summary>An instruct model pulled into Ollama, e.g. qwen2.5:7b-instruct or llama3.1:8b.</summary>
    public string Model { get; set; } = "qwen2.5:7b-instruct";
}

/// <summary>Self-hosted generation via Ollama's /api/chat. Nothing leaves the network.</summary>
public sealed class OllamaChatCompleter(HttpClient http, GenerationOptions options) : IChatCompleter
{
    public string Model => options.Model;

    public Task<string> CompleteAsync(string system, string user, bool jsonMode, CancellationToken ct) =>
        CompleteChatAsync(system, [new ChatTurn(FromUser: true, user)], jsonMode, ct);

    public async Task<string> CompleteChatAsync(
        string system, IReadOnlyList<ChatTurn> turns, bool jsonMode, CancellationToken ct)
    {
        ChatMessage[] messages =
        [
            new("system", system),
            .. turns.Select(t => new ChatMessage(t.FromUser ? "user" : "assistant", t.Text)),
        ];

        var req = new ChatRequest(
            options.Model,
            messages,
            Stream: false,
            Format: jsonMode ? "json" : null,
            Options: new ChatOpts(Temperature: 0.2));

        var url = options.Endpoint.TrimEnd('/') + "/api/chat";
        using var resp = await http.PostAsJsonAsync(url, req, ct);
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<ChatResponse>(ct)
                   ?? throw new InvalidOperationException("Empty Ollama chat response.");
        return body.Message?.Content ?? "";
    }

    private sealed record ChatRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("messages")] ChatMessage[] Messages,
        [property: JsonPropertyName("stream")] bool Stream,
        [property: JsonPropertyName("format"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Format,
        [property: JsonPropertyName("options")] ChatOpts Options);

    private sealed record ChatMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);

    private sealed record ChatOpts([property: JsonPropertyName("temperature")] double Temperature);

    private sealed record ChatResponse([property: JsonPropertyName("message")] ChatMessage? Message);
}
