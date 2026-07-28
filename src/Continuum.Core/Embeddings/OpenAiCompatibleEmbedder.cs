using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Continuum.Core.Embeddings;

public sealed class EmbeddingOptions
{
    /// <summary>"ollama" (default, self-hosted), "local" (no-dependency fallback), or "openai-compatible".</summary>
    public string Provider { get; set; } = "ollama";

    /// <summary>
    /// For "ollama": the Ollama base URL (e.g. http://ollama:11434).
    /// For "openai-compatible": the full embeddings URL (e.g. https://api.openai.com/v1/embeddings).
    /// </summary>
    public string Endpoint { get; set; } = "http://localhost:11434";

    /// <summary>Model name. Must match <see cref="EmbeddingConfig.Dimensions"/> (nomic-embed-text = 768).</summary>
    public string Model { get; set; } = "nomic-embed-text";

    /// <summary>Only used by "openai-compatible". Self-hosted providers need no key.</summary>
    public string ApiKey { get; set; } = "";
}

/// <summary>
/// Calls any OpenAI-embeddings-compatible HTTP API (OpenAI, Voyage, etc.). Selected only when an
/// API key is configured; otherwise the app wires up <see cref="LocalHashEmbedder"/> instead.
/// </summary>
public sealed class OpenAiCompatibleEmbedder(HttpClient http, EmbeddingOptions options) : IEmbedder
{
    public string ProviderName => $"openai-compatible:{options.Model}";
    public int Dimensions => EmbeddingConfig.Dimensions;

    public async Task<float[]> EmbedAsync(string text, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, options.Endpoint)
        {
            Content = JsonContent.Create(new EmbedRequest(text, options.Model, EmbeddingConfig.Dimensions)),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);

        using var resp = await http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<EmbedResponse>(ct)
                   ?? throw new InvalidOperationException("Empty embeddings response.");
        return body.Data.FirstOrDefault()?.Embedding
               ?? throw new InvalidOperationException("No embedding returned.");
    }

    private sealed record EmbedRequest(
        [property: JsonPropertyName("input")] string Input,
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("dimensions")] int Dimensions);

    private sealed record EmbedResponse([property: JsonPropertyName("data")] List<EmbedData> Data);
    private sealed record EmbedData([property: JsonPropertyName("embedding")] float[] Embedding);
}
