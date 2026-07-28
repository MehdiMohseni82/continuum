using Continuum.Core.Embeddings;
using Continuum.Core.Redaction;
using Xunit;

namespace Continuum.Tests;

public class RedactionTests
{
    [Theory]
    [InlineData("my key is AKIAIOSFODNN7EXAMPLE ok", "AWS_KEY")]
    [InlineData("Authorization: Bearer abcdef0123456789abcdef0123", "BEARER")]
    [InlineData("Server=x;Password=SuperSecret1;", "PASSWORD")]
    [InlineData("token: ghp_ABCDEFGHIJKLMNOPQRSTUVWXYZ012345", "GITHUB_TOKEN")]
    public void KnownSecrets_AreRedacted(string input, string label)
    {
        var result = SecretRedactor.Redact(input);
        Assert.True(result.Count >= 1);
        Assert.Contains($"[REDACTED:{label}]", result.Text);
    }

    [Fact]
    public void PlainText_IsUntouched()
    {
        var result = SecretRedactor.Redact("just a normal note about the uploader retry logic");
        Assert.Equal(0, result.Count);
    }

    [Fact]
    public void PasswordRedaction_KeepsTheAssignmentPrefix()
    {
        var result = SecretRedactor.Redact("Password=hunter2xyz;");
        Assert.Contains("Password=", result.Text);
        Assert.DoesNotContain("hunter2xyz", result.Text);
    }

    [Fact]
    public void Detect_ReturnsLabels_WithoutModifying()
    {
        var labels = SecretRedactor.Detect("key is AKIAIOSFODNN7EXAMPLE and Password=abcd1234;");
        Assert.Contains("AWS_KEY", labels);
        Assert.Contains("PASSWORD", labels);
    }

    [Fact]
    public void Detect_CleanText_IsEmpty()
    {
        Assert.Empty(SecretRedactor.Detect("nothing secret here"));
    }
}

public class LocalEmbedderTests
{
    private readonly LocalHashEmbedder _e = new();

    [Fact]
    public async Task ProducesFixedWidthNormalizedVector()
    {
        var v = await _e.EmbedAsync("hello world", default);
        Assert.Equal(EmbeddingConfig.Dimensions, v.Length);

        var norm = Math.Sqrt(v.Sum(x => (double)x * x));
        Assert.InRange(norm, 0.99, 1.01); // L2-normalized
    }

    [Fact]
    public async Task IsDeterministic()
    {
        var a = await _e.EmbedAsync("same text", default);
        var b = await _e.EmbedAsync("same text", default);
        Assert.Equal(a, b);
    }

    [Fact]
    public async Task OverlappingTextIsMoreSimilarThanUnrelated()
    {
        var q = await _e.EmbedAsync("fix the token refresh bug", default);
        var near = await _e.EmbedAsync("the token refresh bug fix", default);
        var far = await _e.EmbedAsync("completely unrelated cooking recipe", default);

        Assert.True(Cosine(q, near) > Cosine(q, far));
    }

    private static double Cosine(float[] a, float[] b)
    {
        double dot = 0;
        for (var i = 0; i < a.Length; i++) dot += a[i] * (double)b[i];
        return dot; // both already normalized
    }
}
