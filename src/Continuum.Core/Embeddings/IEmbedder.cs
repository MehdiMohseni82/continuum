namespace Continuum.Core.Embeddings;

/// <summary>
/// Fixed embedding dimension for the whole system (pgvector column width + HNSW index).
/// Matches the default self-hosted model, Ollama's <c>nomic-embed-text</c> (768).
/// Changing the model to a different width requires updating this AND a migration that
/// alters the <c>Memories.Embedding</c> column, since pgvector columns are fixed-dimension.
/// </summary>
public static class EmbeddingConfig
{
    public const int Dimensions = 768;
}

/// <summary>Turns text into a fixed-width vector. Implementations may be local or hosted.</summary>
public interface IEmbedder
{
    string ProviderName { get; }
    int Dimensions { get; }
    Task<float[]> EmbedAsync(string text, CancellationToken ct);
}
