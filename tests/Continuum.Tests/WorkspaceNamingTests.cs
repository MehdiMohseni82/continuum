using Continuum.Core.Domain;
using Xunit;

namespace Continuum.Tests;

public class WorkspaceNamingTests
{
    [Theory]
    [InlineData("D--dotnet-talk-projects-Geonosys-GeonoAI", "Geonosys-GeonoAI")]
    [InlineData("D--dotnet-talk-projects-agent-talk", "agent-talk")]
    [InlineData("D--dotnet-talk-projects-dotnet-talk-website", "dotnet-talk-website")]
    [InlineData("D--devops-iac", "devops-iac")]
    [InlineData("E--Bramfeld", "Bramfeld")]
    public void PrettifyStripsDriveAndContainerPrefixes(string key, string expected)
    {
        Assert.Equal(expected, WorkspaceNaming.Prettify(key));
    }

    [Theory]
    [InlineData("D--")]          // a session run from a drive root — no tail to keep
    [InlineData("")]
    [InlineData("   ")]
    public void PrettifyNeverReturnsEmpty(string key)
    {
        Assert.Equal(key, WorkspaceNaming.Prettify(key));
    }
}
