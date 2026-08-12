using System.Text.RegularExpressions;

namespace Continuum.Core.Domain;

/// <summary>
/// Turns a raw Claude Code projectKey (the mangled "D--dotnet-talk-projects-Foo" directory name)
/// into a readable default DisplayName. Users can still rename freely; this just stops brand-new
/// workspaces from defaulting to the ugly full key. Conservative on purpose: it strips the drive
/// prefix and a common container prefix but keeps the meaningful tail intact.
/// </summary>
public static partial class WorkspaceNaming
{
    [GeneratedRegex(@"^[A-Za-z]--")] private static partial Regex DrivePrefix();

    // The shared parent folder most projects live under; stripping it removes noise, not meaning.
    private const string ContainerPrefix = "dotnet-talk-projects-";

    public static string Prettify(string projectKey)
    {
        if (string.IsNullOrWhiteSpace(projectKey)) return projectKey;

        var s = DrivePrefix().Replace(projectKey, "");
        if (s.StartsWith(ContainerPrefix, StringComparison.Ordinal))
            s = s[ContainerPrefix.Length..];

        // Never return an empty name (e.g. a bare "D--" for a session run from a drive root).
        return string.IsNullOrWhiteSpace(s) ? projectKey : s;
    }
}
