using System.Net;
using Continuum.Core.Contracts;
using Continuum.Core.Domain;

namespace Continuum.Cli;

/// <summary>
/// <c>continuum project</c> — show, and <c>continuum project set</c> — declare, this repo's workspace key.
///
/// <para>
/// The key decides which workspace a session's memory and history land in. Derived from the cwd, it
/// is different on every machine, so the same repo on a Mac and on Windows accumulates two separate
/// memories that never meet. Declaring a key in a committed <c>.continuum-project</c> file fixes that
/// for every machine at once.
/// </para>
/// <para>
/// <c>set</c> also re-keys the existing workspace on the backend, which is the part that is easy to
/// forget and expensive to get wrong: declare a key without it and the repo starts a fresh, empty
/// workspace while everything it had learned stays behind under the old one.
/// </para>
/// </summary>
public static class ProjectCommand
{
    public static async Task<int> RunAsync(string[] args, CancellationToken ct)
    {
        var sub = args.Length > 0 ? args[0].ToLowerInvariant() : "show";
        return sub switch
        {
            "show" => await ShowAsync(Directory.GetCurrentDirectory(), ct),
            "set" when args.Length >= 2 => await SetAsync(args[1], Dir(args, 2), ct),
            "set" => Err("Usage: continuum project set <key> [dir]"),
            _ => await ShowAsync(Dir(args, 0), ct),
        };

        static string Dir(string[] a, int i) =>
            a.Length > i ? Path.GetFullPath(a[i]) : Directory.GetCurrentDirectory();

        static int Err(string m) { Console.Error.WriteLine(m); return 1; }
    }

    /// <summary>The key Claude Code derives from a path: every non-alphanumeric character becomes a dash.</summary>
    private static string Derive(string dir) =>
        string.Concat(dir.Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-'));

    private static async Task<int> ShowAsync(string dir, CancellationToken ct)
    {
        var derived = Derive(dir);
        var declared = ProjectKey.ReadMarker(dir);
        var effective = declared ?? derived;

        Console.WriteLine($"Folder      {dir}");
        Console.WriteLine($"Derived key {derived}");
        Console.WriteLine(declared is null
            ? $"Declared    (none — no {ProjectKey.MarkerFileName} file)"
            : $"Declared    {declared}   ({Path.Combine(dir, ProjectKey.MarkerFileName)})");
        Console.WriteLine($"In use      {effective}");
        Console.WriteLine();

        var cfg = Config.Load();
        if (cfg is null)
        {
            Console.WriteLine("Continuum is not configured, so the backend side can't be checked. Run `continuum doctor`.");
            return 0;
        }

        using var api = new Api(cfg);
        List<WorkspaceDto>? spaces;
        try { spaces = await api.GetAsync<List<WorkspaceDto>>("/api/workspaces", ct); }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Could not reach {cfg.Backend}: {ex.Message}");
            return 1;
        }

        var live = spaces?.FirstOrDefault(w => w.ProjectKey == effective);
        var stranded = declared is null ? null : spaces?.FirstOrDefault(w => w.ProjectKey == derived);

        if (live is not null)
            Console.WriteLine($"Workspace   {live.DisplayName} — {live.SessionCount} session(s)");
        else
            Console.WriteLine("Workspace   none yet on the backend (it appears once a session is ingested)");

        // The failure this command exists to catch: a declared key with the real history still filed
        // under the derived one. Silent, and it looks exactly like "memory stopped working".
        if (stranded is not null)
        {
            Console.WriteLine();
            Console.WriteLine($"  ⚠ {stranded.SessionCount} session(s) are still filed under the derived key.");
            Console.WriteLine($"    Move them over with:  continuum project set {declared}");
        }

        if (declared is null)
        {
            Console.WriteLine();
            Console.WriteLine("This repo has no declared key, so the same checkout on another machine will");
            Console.WriteLine("get a workspace of its own. Declare one (and commit it) with:");
            Console.WriteLine($"    continuum project set {Suggest(dir)}");
        }

        return 0;
    }

    private static async Task<int> SetAsync(string rawKey, string dir, CancellationToken ct)
    {
        var key = ProjectKey.Sanitize(rawKey);
        if (key is null)
        {
            Console.Error.WriteLine("That isn't a usable project key.");
            return 1;
        }

        if (!Directory.Exists(dir))
        {
            Console.Error.WriteLine($"No such folder: {dir}");
            return 1;
        }

        var marker = Path.Combine(dir, ProjectKey.MarkerFileName);
        var previous = ProjectKey.ReadMarker(dir);
        try
        {
            File.WriteAllText(marker, $"""
                # Continuum: the workspace this repo's sessions belong to.
                # Committed on purpose — it is what makes memory follow the repo between machines
                # instead of being derived from wherever the checkout happens to sit on disk.
                {key}

                """);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Could not write {marker}: {ex.Message}");
            return 1;
        }

        Console.WriteLine($"Wrote {marker}");
        Console.WriteLine($"  key: {key}");

        var cfg = Config.Load();
        if (cfg is null)
        {
            Console.WriteLine();
            Console.WriteLine("Continuum is not configured here, so the existing workspace was not re-keyed.");
            Console.WriteLine("Run `continuum doctor`, then `continuum project set` again.");
            return 0;
        }

        // Move the existing workspace over. The one we want is whichever key was in force a moment
        // ago — the previously declared one if there was one, else the derived one.
        var was = previous ?? Derive(dir);
        using var api = new Api(cfg);

        List<WorkspaceDto>? spaces;
        try { spaces = await api.GetAsync<List<WorkspaceDto>>("/api/workspaces", ct); }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Could not reach {cfg.Backend}: {ex.Message}");
            Console.Error.WriteLine("The marker is written; re-run this command to move the history.");
            return 1;
        }

        if (spaces?.FirstOrDefault(w => w.ProjectKey == key) is { } already)
        {
            Console.WriteLine();
            Console.WriteLine($"Backend: '{key}' already exists ({already.SessionCount} session(s)) — this repo now joins it.");
            return 0;
        }

        if (spaces?.FirstOrDefault(w => w.ProjectKey == was) is not { } old)
        {
            Console.WriteLine();
            Console.WriteLine("Backend: no existing workspace to move; the next session creates it under the new key.");
            return 0;
        }

        var status = await api.PatchAsync(
            $"/api/workspaces/{old.Id}/project-key", new RekeyWorkspaceRequest(key), ct);

        Console.WriteLine();
        switch (status)
        {
            case HttpStatusCode.NoContent:
                Console.WriteLine($"Backend: moved '{old.DisplayName}' ({old.SessionCount} session(s)) onto '{key}'.");
                Console.WriteLine("Commit the marker file and the same repo on any other machine joins this workspace.");
                return 0;
            case HttpStatusCode.Conflict:
                Console.Error.WriteLine($"Backend: another workspace already uses '{key}'. Pick a different key.");
                return 1;
            case HttpStatusCode.Forbidden:
                Console.Error.WriteLine("Backend: re-keying a workspace is admin-only. The marker is written; ask an admin to move the history.");
                return 1;
            default:
                Console.Error.WriteLine($"Backend: re-key failed ({(int)status} {status}). The marker is written.");
                return 1;
        }
    }

    /// <summary>A plausible key to offer: the folder name, with its parent when that reads like an org.</summary>
    private static string Suggest(string dir)
    {
        var name = new DirectoryInfo(dir).Name;
        var parent = new DirectoryInfo(dir).Parent?.Name;
        return string.IsNullOrWhiteSpace(parent) ? name : $"{parent}/{name}";
    }
}
