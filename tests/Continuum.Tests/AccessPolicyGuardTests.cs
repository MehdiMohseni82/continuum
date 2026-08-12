using System.Runtime.CompilerServices;
using Xunit;

namespace Continuum.Tests;

/// <summary>
/// Keeps the authorization seam from eroding. Collapsing 45 hand-written copies of the visibility rule
/// into one policy is only worth doing if the forty-sixth can't quietly appear, so these tests read the
/// Host's source and fail the build when a service starts deciding visibility for itself.
/// <para>
/// They are intentionally source-level rather than reflective: the failure being prevented is a person
/// writing <c>admin || OwnerId == uid</c> into a new query, which no runtime check would catch until it
/// had already served someone else's data.
/// </para>
/// </summary>
public class AccessPolicyGuardTests
{
    /// <summary>Locates the repository from this file's own path, so it works regardless of build output layout.</summary>
    private static string ServicesDirectory([CallerFilePath] string thisFile = "")
    {
        var repo = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));
        return Path.Combine(repo, "src", "Continuum.Host", "Services");
    }

    private static IEnumerable<(string Name, string Text)> ServiceSources()
    {
        var dir = ServicesDirectory();
        Assert.True(Directory.Exists(dir), $"Expected the Host services directory at {dir}");
        foreach (var path in Directory.EnumerateFiles(dir, "*.cs"))
            yield return (Path.GetFileName(path), File.ReadAllText(path));
    }

    [Fact]
    public void NoServiceWritesTheVisibilityRuleInline()
    {
        var offenders = ServiceSources()
            .Where(f => f.Text.Contains("admin ||") || f.Text.Contains("Admin ||"))
            .Select(f => f.Name)
            .ToList();

        Assert.True(offenders.Count == 0,
            "These services decide visibility inline instead of asking IAccessPolicy: " +
            string.Join(", ", offenders) +
            ". Add the rule to AccessPolicy and call it, so tenancy stays a one-file change.");
    }

    [Fact]
    public void NoServiceConsultsTheAdminFlagDirectly()
    {
        // Role checks belong on endpoints (operational: managing users, tokens, rooms). A service
        // reaching for IsAdmin is deciding data access by role, which is what phase 6 removes.
        var offenders = ServiceSources()
            .Where(f => f.Text.Contains("IsAdmin"))
            .Select(f => f.Name)
            .ToList();

        Assert.True(offenders.Count == 0,
            "These services read ICurrentUser.IsAdmin directly: " + string.Join(", ", offenders) +
            ". Data visibility must come from IAccessPolicy, not from a role check.");
    }

    [Fact]
    public void NoServiceBuildsAnOwnershipClauseInRawSql()
    {
        // Two services legitimately run raw SQL; they must take the clause from the policy rather than
        // spelling out OwnerId/Shared themselves, where no compiler would ever see it drift.
        var offenders = ServiceSources()
            .Where(f => f.Text.Contains("\\\"OwnerId\\\"") || f.Text.Contains("\"\"OwnerId\"\""))
            .Select(f => f.Name)
            .ToList();

        Assert.True(offenders.Count == 0,
            "These services name OwnerId inside raw SQL: " + string.Join(", ", offenders) +
            ". Use IAccessPolicy.VisibleSessionsSql so the rule has one definition.");
    }

    [Fact]
    public void TheGuardIsActuallyLookingAtSomething()
    {
        // A guard that silently scans nothing passes forever. Prove it found the real services.
        var names = ServiceSources().Select(f => f.Name).ToList();

        Assert.Contains("HistoryService.cs", names);
        Assert.Contains("MemoryService.cs", names);
        Assert.Contains("RoomService.cs", names);
        Assert.Contains("TokenAnalyticsService.cs", names);
        Assert.True(names.Count > 15, $"Expected the full service set, found only {names.Count}.");
    }
}
