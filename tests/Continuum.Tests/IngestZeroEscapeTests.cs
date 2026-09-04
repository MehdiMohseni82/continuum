using System.Text.Json;
using Continuum.Core.Ingest;
using Xunit;

namespace Continuum.Tests;

/// <summary>
/// Postgres refuses an escaped zero inside a jsonb string, and RawJson is jsonb. A transcript acquires
/// one honestly — any session that discusses or tests control characters writes one into its own JSONL
/// — and the result was a 500 on the batch, a skipped file, and a cursor that never advanced, so the
/// rest of that session's history was lost silently.
/// </summary>
public class IngestZeroEscapeTests
{
    private const string Zero = "\\u0000";

    private static string Line(string text) =>
        $"{{\"type\":\"user\",\"uuid\":\"11111111-1111-1111-1111-111111111111\"," +
        $"\"message\":{{\"role\":\"user\",\"content\":\"{text}\"}}}}";

    [Fact]
    public void AZeroEscapeIsRemovedFromTheRawJson()
    {
        var evt = JsonlParser.ParseLine(Line($"a{Zero}b"), Guid.NewGuid(), "k");

        Assert.NotNull(evt);
        var raw = evt!.Raw.GetRawText();
        Assert.DoesNotContain(Zero, raw, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheSurroundingTextSurvives()
    {
        // Substituted, not deleted: an excerpt still has to read sensibly.
        var evt = JsonlParser.ParseLine(Line($"before{Zero}after"), Guid.NewGuid(), "k");

        Assert.NotNull(evt);
        Assert.Contains("before", evt!.Text);
        Assert.Contains("after", evt.Text);
    }

    [Theory]
    [InlineData("\\u0000")]
    [InlineData("\\U0000")]
    [InlineData("\\u0000\\u0000")]
    public void EveryCasingAndRepetitionIsHandled(string escape)
    {
        var evt = JsonlParser.ParseLine(Line($"x{escape}y"), Guid.NewGuid(), "k");

        Assert.NotNull(evt);
        Assert.DoesNotContain("u0000", evt!.Raw.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheResultIsStillValidJsonAndKeepsItsFields()
    {
        var evt = JsonlParser.ParseLine(Line($"hello{Zero}world"), Guid.NewGuid(), "proj");

        Assert.NotNull(evt);
        using var doc = JsonDocument.Parse(evt!.Raw.GetRawText());   // would throw if we broke the JSON
        Assert.Equal("user", doc.RootElement.GetProperty("type").GetString());
        Assert.Equal("user", evt.Role);
        Assert.Equal("proj", evt.ProjectKey);
    }

    [Fact]
    public void LinesWithoutOneAreUntouched()
    {
        const string line = "{\"type\":\"user\",\"message\":{\"role\":\"user\",\"content\":\"plain\"}}";
        Assert.Same(line, JsonlParser.StripZeroEscapes(line));
    }

    [Fact]
    public void AnOtherwiseLegalLowEscapeIsLeftAlone()
    {
        // Only the zero escape is rejected by jsonb; a tab escape is perfectly storable and must
        // survive, or we would be corrupting transcripts to fix a narrower problem.
        var line = Line("a\\u0009b");
        Assert.Same(line, JsonlParser.StripZeroEscapes(line));
    }
}
