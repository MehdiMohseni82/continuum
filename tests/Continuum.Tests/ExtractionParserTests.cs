using Continuum.Core.Domain;
using Continuum.Core.Generation;
using Xunit;

namespace Continuum.Tests;

public class ExtractionParserTests
{
    [Fact]
    public void ParsesMemoriesObject()
    {
        var json = """
            {"memories":[
              {"type":"Feedback","content":"User prefers concise C#/.NET answers"},
              {"type":"Project","content":"Continuum uses pgvector for recall"}
            ]}
            """;
        var items = ExtractionParser.Parse(json);
        Assert.Equal(2, items.Count);
        Assert.Equal(MemoryType.Feedback, items[0].Type);
        Assert.Contains("concise", items[0].Content);
    }

    [Fact]
    public void ParsesBareArray()
    {
        var items = ExtractionParser.Parse("""[{"type":"Reference","content":"docs at example.com"}]""");
        Assert.Single(items);
        Assert.Equal(MemoryType.Reference, items[0].Type);
    }

    [Fact]
    public void StripsCodeFencesAndLeadingProse()
    {
        var raw = "Here you go:\n```json\n{\"memories\":[{\"type\":\"Project\",\"content\":\"x\"}]}\n```";
        var items = ExtractionParser.Parse(raw);
        Assert.Single(items);
        Assert.Equal(MemoryType.Project, items[0].Type);
    }

    [Fact]
    public void UnknownTypeDefaultsToProject_AndSkipsEmpty()
    {
        var json = """{"memories":[{"type":"weird","content":"keep"},{"type":"Project","content":"  "}]}""";
        var items = ExtractionParser.Parse(json);
        Assert.Single(items);
        Assert.Equal(MemoryType.Project, items[0].Type);
        Assert.Equal("keep", items[0].Content);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json at all")]
    [InlineData("{}")]
    public void JunkReturnsEmpty(string raw) => Assert.Empty(ExtractionParser.Parse(raw));

    [Fact]
    public void RespectsMaxItems()
    {
        var json = """{"memories":[{"type":"Project","content":"a"},{"type":"Project","content":"b"},{"type":"Project","content":"c"}]}""";
        Assert.Equal(2, ExtractionParser.Parse(json, maxItems: 2).Count);
    }
}
