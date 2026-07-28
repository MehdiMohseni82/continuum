using System.Text.Json;
using Continuum.Core.Ingest;
using Xunit;

namespace Continuum.Tests;

public class JsonlParserTests
{
    private static readonly Guid Session = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private const string Project = "D--example-project";

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankLines_ReturnNull(string line)
    {
        Assert.Null(JsonlParser.ParseLine(line, Session, Project));
    }

    [Fact]
    public void UserMessage_ExtractsRoleAndText()
    {
        var line = """
            {"type":"user","uuid":"22222222-2222-2222-2222-222222222222","sessionId":"11111111-1111-1111-1111-111111111111","message":{"role":"user","content":"fix the IAM token refresh"},"timestamp":"2026-01-05T10:00:00Z","version":"1.2.3"}
            """;

        var evt = JsonlParser.ParseLine(line, Session, Project)!;

        Assert.Equal("user", evt.Type);
        Assert.Equal("user", evt.Role);
        Assert.Equal("fix the IAM token refresh", evt.Text);
        Assert.Equal("1.2.3", evt.CcVersion);
        Assert.Equal(Guid.Parse("22222222-2222-2222-2222-222222222222"), evt.Uuid);
    }

    [Fact]
    public void ContentArray_IsFlattened()
    {
        var line = """
            {"type":"assistant","message":{"role":"assistant","content":[{"type":"text","text":"first"},{"type":"text","text":"second"}]},"timestamp":"2026-01-05T10:00:00Z"}
            """;

        var evt = JsonlParser.ParseLine(line, Session, Project)!;

        Assert.Contains("first", evt.Text);
        Assert.Contains("second", evt.Text);
    }

    [Fact]
    public void MissingUuid_IsSynthesizedDeterministically()
    {
        var line = """{"type":"system","content":"note"}""";

        var a = JsonlParser.ParseLine(line, Session, Project)!;
        var b = JsonlParser.ParseLine(line, Session, Project)!;

        Assert.NotEqual(Guid.Empty, a.Uuid);
        Assert.Equal(a.Uuid, b.Uuid); // stable across calls → idempotent ingest
    }

    [Fact]
    public void InvalidJson_IsPreservedNotDropped()
    {
        var evt = JsonlParser.ParseLine("this is not json {", Session, Project)!;

        Assert.Equal("unparsed", evt.Type);
        Assert.Equal(Session, evt.SessionId);
        Assert.Equal(JsonValueKind.Object, evt.Raw.ValueKind);
        Assert.True(evt.Raw.TryGetProperty("_unparsed", out _));
    }

    [Fact]
    public void AiTitle_SetsTitle()
    {
        var line = """{"type":"ai-title","aiTitle":"Refactoring the daemon"}""";

        var evt = JsonlParser.ParseLine(line, Session, Project)!;

        Assert.Equal("Refactoring the daemon", evt.Title);
    }

    [Fact]
    public void SessionId_FallsBackToFileWhenLineHasNone()
    {
        var line = """{"type":"system","content":"x"}""";

        var evt = JsonlParser.ParseLine(line, Session, Project)!;

        Assert.Equal(Session, evt.SessionId);
    }
}
