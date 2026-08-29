namespace Continuum.Core.Domain;

/// <summary>
/// Resolves the workspace key for a working directory.
///
/// <para>
/// By default the key is the Claude Code project directory name — the cwd with every non-alphanumeric
/// character replaced by a dash. That name encodes an absolute path, so the same repo checked out on
/// a Mac and on Windows produces two different keys, two different workspaces, and two disjoint piles
/// of memory. A session on one machine then remembers nothing the other learned.
/// </para>
/// <para>
/// A repo can instead declare its own key in a <c>.continuum-project</c> file at its root. Commit
/// that file and every machine agrees, because the identity travels with the repo instead of being
/// inferred from where it happens to sit on disk.
/// </para>
///
/// The bash and PowerShell SessionStart hooks reimplement <see cref="ReadMarker"/> in four lines each;
/// they run before any of this assembly is loaded. Keep the three in step.
/// </summary>
public static class ProjectKey
{
    /// <summary>The file a repo uses to declare its own workspace key.</summary>
    public const string MarkerFileName = ".continuum-project";

    /// <summary>Workspaces.ProjectKey is capped at 512 chars in the schema.</summary>
    public const int MaxLength = 512;

    /// <summary>
    /// The key for <paramref name="workingDirectory"/>: the declared one if the repo declares it,
    /// otherwise <paramref name="fallback"/> (the project directory name the caller already has).
    /// </summary>
    public static string Resolve(string? workingDirectory, string fallback) =>
        ReadMarker(workingDirectory) ?? fallback;

    /// <summary>
    /// The key declared by <c>.continuum-project</c> in <paramref name="workingDirectory"/>, or null
    /// if there is no such file, it holds nothing usable, or it can't be read. Never throws: an
    /// unreadable marker must degrade to the derived key, not break ingest.
    /// </summary>
    public static string? ReadMarker(string? workingDirectory)
    {
        if (string.IsNullOrWhiteSpace(workingDirectory)) return null;

        try
        {
            var path = Path.Combine(workingDirectory, MarkerFileName);
            if (!File.Exists(path)) return null;

            foreach (var line in File.ReadLines(path))
            {
                var key = Sanitize(line);
                if (key is not null) return key;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Unreadable marker: fall back to the derived key rather than losing the session.
        }

        return null;
    }

    /// <summary>
    /// A marker line reduced to a key, or null if the line carries none. Blank lines and <c>#</c>
    /// comments are skipped so the file can explain itself to whoever opens it next.
    /// </summary>
    public static string? Sanitize(string? line)
    {
        var s = line?.Trim();
        if (string.IsNullOrEmpty(s) || s.StartsWith('#')) return null;

        // Control characters would travel into a URL query and a database key. Drop them.
        s = new string([.. s.Where(c => !char.IsControl(c))]).Trim();
        if (s.Length == 0) return null;

        return s.Length > MaxLength ? s[..MaxLength] : s;
    }
}
