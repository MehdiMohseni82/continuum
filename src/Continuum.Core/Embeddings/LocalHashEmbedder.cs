using System.Security.Cryptography;
using System.Text;

namespace Continuum.Core.Embeddings;

/// <summary>
/// A deterministic, dependency-free embedder used when no hosted provider is configured.
/// It hashes token trigrams into a fixed-width bag-of-features vector and L2-normalizes it.
/// This is NOT semantic — it captures lexical overlap only — but it keeps the entire memory
/// pipeline (store, index, cosine search) runnable and testable without an API key.
/// Swap in <see cref="OpenAiCompatibleEmbedder"/> for real semantic recall.
/// </summary>
public sealed class LocalHashEmbedder : IEmbedder
{
    public string ProviderName => "local-hash";
    public int Dimensions => EmbeddingConfig.Dimensions;

    public Task<float[]> EmbedAsync(string text, CancellationToken ct)
    {
        var vec = new float[Dimensions];
        if (!string.IsNullOrWhiteSpace(text))
        {
            foreach (var token in Tokenize(text))
            {
                var h = BitConverter.ToUInt32(SHA256.HashData(Encoding.UTF8.GetBytes(token)), 0);
                var idx = (int)(h % (uint)Dimensions);
                var sign = (h & 0x80000000) == 0 ? 1f : -1f;
                vec[idx] += sign;
            }
            Normalize(vec);
        }
        return Task.FromResult(vec);
    }

    private static IEnumerable<string> Tokenize(string text)
    {
        var words = text.ToLowerInvariant()
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        foreach (var w in words)
        {
            yield return w;
            for (var i = 0; i + 3 <= w.Length; i++)
                yield return w.Substring(i, 3); // character trigrams for fuzzy overlap
        }
    }

    private static void Normalize(float[] v)
    {
        double sum = 0;
        foreach (var x in v) sum += x * (double)x;
        var norm = Math.Sqrt(sum);
        if (norm <= 0) return;
        for (var i = 0; i < v.Length; i++) v[i] = (float)(v[i] / norm);
    }
}
