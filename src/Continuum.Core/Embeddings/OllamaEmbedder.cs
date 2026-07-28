using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Continuum.Core.Embeddings;

/// <summary>
/// Self-hosted embeddings via a local Ollama server running an open-source embedding model
/// (default <c>nomic-embed-text</c>, 768-dim). Nothing leaves your network — the right choice
/// when transcripts contain secrets. Endpoint is the Ollama base URL (e.g. http://ollama:11434).
/// </summary>
public sealed class OllamaEmbedder(HttpClient http, EmbeddingOptions options) : IEmbedder
{
    public string ProviderName => $"ollama:{options.Model}";
    public int Dimensions => EmbeddingConfig.Dimensions;

    public async Task<float[]> EmbedAsync(string text, CancellationToken ct)
    {
        var url = options.Endpoint.TrimEnd('/') + "/api/embeddings";
        using var resp = await http.PostAsJsonAsync(url, new OllamaRequest(options.Model, text), ct);
        resp.EnsureSuccessStatusCode();

        var body = await resp.Content.ReadFromJsonAsync<OllamaResponse>(ct)
                   ?? throw new InvalidOperationException("Empty embeddings response from Ollama.");
        var vector = body.Embedding
                     ?? throw new InvalidOperationException("Ollama returned no embedding — is the model pulled?");

        if (vector.Length != EmbeddingConfig.Dimensions)
            throw new InvalidOperationException(
                $"Model '{options.Model}' returned {vector.Length}-dim vectors but the store expects " +
                $"{EmbeddingConfig.Dimensions}. Update EmbeddingConfig.Dimensions + the Memories.Embedding " +
                "column to match this model, or choose a model with the expected width.");

        return vector;
    }

    private sealed record OllamaRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("prompt")] string Prompt);

    private sealed record OllamaResponse(
        [property: JsonPropertyName("embedding")] float[]? Embedding);
}
