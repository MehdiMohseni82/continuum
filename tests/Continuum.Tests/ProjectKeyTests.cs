using Continuum.Core.Domain;
using Xunit;

namespace Continuum.Tests;

public class ProjectKeyTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("continuum-projectkey").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
        GC.SuppressFinalize(this);
    }

    private string WriteMarker(string content)
    {
        File.WriteAllText(Path.Combine(_dir, ProjectKey.MarkerFileName), content);
        return _dir;
    }

    [Fact]
    public void NoMarkerFallsBackToTheDerivedKey()
    {
        Assert.Equal("D--projects-Foo", ProjectKey.Resolve(_dir, "D--projects-Foo"));
        Assert.Null(ProjectKey.ReadMarker(_dir));
    }

    [Fact]
    public void AMarkerWins()
    {
        WriteMarker("dotnet-talk/continuum\n");
        Assert.Equal("dotnet-talk/continuum", ProjectKey.Resolve(_dir, "D--projects-Foo"));
    }

    [Fact]
    public void TheSameMarkerYieldsTheSameKeyFromUnrelatedPaths()
    {
        // The whole point: a Mac path and a Windows path must land on one workspace.
        WriteMarker("dotnet-talk/continuum");
        var fromMac = ProjectKey.Resolve(_dir, "-Users-mehdi-dev-DotNetTalk-Continuum");
        var fromWindows = ProjectKey.Resolve(_dir, "D--dotnet-talk-projects-Continuum");
        Assert.Equal(fromMac, fromWindows);
    }

    [Fact]
    public void CommentsAndBlankLinesAreSkipped()
    {
        WriteMarker("# Continuum: this repo's workspace\n\n   \ndotnet-talk/continuum\nignored\n");
        Assert.Equal("dotnet-talk/continuum", ProjectKey.ReadMarker(_dir));
    }

    [Fact]
    public void AMarkerWithNothingUsableFallsBack()
    {
        WriteMarker("# only a comment\n\n");
        Assert.Null(ProjectKey.ReadMarker(_dir));
        Assert.Equal("derived", ProjectKey.Resolve(_dir, "derived"));
    }

    [Fact]
    public void WhitespaceAndCarriageReturnsAreTrimmed()
    {
        // A file written on Windows and read on a Mac must not yield a key with a trailing CR,
        // which would be a different workspace that looks identical in every log.
        WriteMarker("  dotnet-talk/continuum  \r\n");
        Assert.Equal("dotnet-talk/continuum", ProjectKey.ReadMarker(_dir));
    }

    [Fact]
    public void KeysAreCappedToTheSchemaLimit()
    {
        WriteMarker(new string('k', ProjectKey.MaxLength + 50));
        Assert.Equal(ProjectKey.MaxLength, ProjectKey.ReadMarker(_dir)!.Length);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AnEmptyWorkingDirectoryIsNotAMarkerLookup(string? dir)
    {
        Assert.Null(ProjectKey.ReadMarker(dir));
        Assert.Equal("derived", ProjectKey.Resolve(dir, "derived"));
    }

    [Fact]
    public void ADirectoryThatDoesNotExistDegradesQuietly()
    {
        var missing = Path.Combine(_dir, "nope", "gone");
        Assert.Null(ProjectKey.ReadMarker(missing));
    }

    [Theory]
    [InlineData("#comment", null)]
    [InlineData("  ", null)]
    [InlineData("a\tb", "ab")]     // control characters would travel into a URL and a DB key
    [InlineData(" key ", "key")]
    public void SanitizeReducesALineToAKeyOrNothing(string line, string? expected)
    {
        Assert.Equal(expected, ProjectKey.Sanitize(line));
    }
}
