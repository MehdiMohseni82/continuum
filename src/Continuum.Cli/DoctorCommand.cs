using Continuum.Core.Contracts;

namespace Continuum.Cli;

/// <summary>
/// <c>continuum doctor</c> — says out loud what is and isn't wired up.
///
/// Every one of these checks corresponds to a way Continuum currently fails *silently*: hooks that
/// swallow curl errors, an MCP server that was never registered, a slash command left pointing at a
/// path from another machine. A session in any of those states looks completely normal.
/// </summary>
public static class DoctorCommand
{
    private static int _problems;

    public static async Task<int> RunAsync(CancellationToken ct)
    {
        _problems = 0;
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        Console.WriteLine("Continuum doctor");
        Console.WriteLine("────────────────");

        var cfg = await CheckConfigAsync(ct);
        CheckMcp(home);
        CheckHooks(home);
        CheckSlashCommands(home);
        CheckRelayHere();

        Console.WriteLine();
        if (_problems == 0) Console.WriteLine("All checks passed.");
        else Console.WriteLine($"{_problems} problem{(_problems == 1 ? "" : "s")} above. Fix the topmost one first — the rest often follow.");

        return cfg is null || _problems > 0 ? 1 : 0;
    }

    private static async Task<Config?> CheckConfigAsync(CancellationToken ct)
    {
        Console.WriteLine();
        Console.WriteLine("Configuration");

        var cfg = Config.Load();
        if (cfg is null)
        {
            Bad("no backend/token", $"set CONTINUUM_BACKEND and CONTINUUM_TOKEN, or write {Config.Path_}. "
                                    + "Re-running scripts/install-agent.sh does this for you.");
            return null;
        }

        var fromEnv = Environment.GetEnvironmentVariable("CONTINUUM_BACKEND") is not null;
        Ok("backend", $"{cfg.Backend}  (from {(fromEnv ? "environment" : Config.Path_)})");
        Ok("token", $"{Mask(cfg.Token)}  ({cfg.Token.Length} chars)");
        if (cfg.Agent is { Length: > 0 }) Ok("agent name", cfg.Agent);
        else Warn("agent name", "not set — room commands need one typed by hand each time.");

        // Legacy shared token: it authenticates as the bootstrap admin, so nothing this machine does
        // is attributable to a person. Fine solo, wrong the moment a colleague joins.
        if (cfg.Token.StartsWith("dev-local-token", StringComparison.OrdinalIgnoreCase))
            Warn("token kind", "this is the legacy shared token, not a personal one. Create a PAT in the web UI (Settings → Tokens).");

        Console.WriteLine();
        Console.WriteLine("Backend");
        try
        {
            using var api = new Api(cfg, TimeSpan.FromSeconds(15));
            var me = await api.GetAsync<MeDto>("/api/auth/me", ct);
            if (me is null) Bad("identity", "the backend answered but returned nothing for /api/auth/me.");
            else Ok("authenticated", $"{me.DisplayName} <{me.Email}>  role={me.Role}{(me.IsLegacy ? "  (legacy token)" : "")}");

            var rooms = await api.GetAsync<List<RoomDto>>("/api/rooms", ct);
            var open = rooms?.Count(r => r.Status == "open") ?? 0;
            Ok("rooms visible", $"{rooms?.Count ?? 0} ({open} open)");
        }
        catch (ApiException ex) when (ex.Status == System.Net.HttpStatusCode.Unauthorized)
        {
            Bad("authentication", "the backend rejected this token (401). It may be revoked or expired — create a new PAT.");
        }
        catch (Exception ex)
        {
            Bad("reachability", $"could not reach {cfg.Backend}: {ex.Message}");
        }

        return cfg;
    }

    private static void CheckMcp(string home)
    {
        Console.WriteLine();
        Console.WriteLine("MCP registration");

        // Claude Code keeps servers in ~/.claude.json, per project and globally.
        var claudeJson = Path.Combine(home, ".claude.json");
        if (Contains(claudeJson, "\"continuum\"")) Ok("Claude Code", claudeJson);
        else Warn("Claude Code", $"no `continuum` server found in {claudeJson} — run `claude mcp add`, or re-run scripts/install-agent.sh.");

        var codex = Path.Combine(home, ".codex", "config.toml");
        if (Contains(codex, "mcp_servers.continuum")) Ok("Codex", codex);
        else Warn("Codex", $"no [mcp_servers.continuum] block in {codex}.");

        var cursor = Path.Combine(home, ".cursor", "mcp.json");
        if (Contains(cursor, "\"continuum\"")) Ok("Cursor", cursor);
        else Warn("Cursor", $"no `continuum` server in {cursor}.");
    }

    private static void CheckHooks(string home)
    {
        Console.WriteLine();
        Console.WriteLine("Session hooks (memory + history)");

        var settings = Path.Combine(home, ".claude", "settings.json");
        if (!File.Exists(settings)) { Warn("settings.json", $"{settings} does not exist — no hooks are installed."); return; }

        var text = File.ReadAllText(settings);
        Check("SessionStart", text.Contains("session-start"), "injects your memories and last checkpoint into every new session");
        Check("PreCompact", text.Contains("pre-compact"), "saves a checkpoint before context is compacted");

        static void Check(string name, bool present, string what)
        {
            if (present) Ok(name, what);
            else Warn(name, $"not registered — {what}. Re-run scripts/install-agent.sh.");
        }
    }

    private static void CheckSlashCommands(string home)
    {
        Console.WriteLine();
        Console.WriteLine("Slash commands");

        var dir = Path.Combine(home, ".claude", "commands");
        foreach (var name in new[] { "continuum-joinroom", "continuum-leaveroom" })
        {
            var path = Path.Combine(dir, name + ".md");
            if (!File.Exists(path)) { Warn($"/{name}", "not installed — run `continuum setup-relay`."); continue; }

            var body = File.ReadAllText(path);
            // The exact failure that made rooms unusable on this machine: settings sync carries a
            // command file with another machine's absolute path, and it silently does nothing.
            if (body.Contains(".ps1") || body.Contains("C:/") || body.Contains(@"C:\"))
                Bad($"/{name}", $"{path} points at a Windows path from another machine. Run `continuum setup-relay` to replace it.");
            else
                Ok($"/{name}", path);
        }
    }

    private static void CheckRelayHere()
    {
        Console.WriteLine();
        Console.WriteLine("Room relay (this folder)");

        var cwd = Directory.GetCurrentDirectory();
        if (RoomCommands.StopHookInstalled(cwd))
            Ok("Stop hook", $"registered for {cwd}");
        else
            Warn("Stop hook", $"not registered for {cwd} — joining a room here would post nothing. Run `continuum setup-relay`.");
    }

    private static bool Contains(string path, string needle)
    {
        try { return File.Exists(path) && File.ReadAllText(path).Contains(needle); }
        catch { return false; }
    }

    /// <summary>Enough of the token to recognise which one it is, never enough to use.</summary>
    private static string Mask(string token) =>
        token.Length <= 10 ? new string('•', token.Length) : token[..6] + new string('•', 8);

    private static void Ok(string label, string detail) => Console.WriteLine($"  ok    {label,-20} {detail}");

    private static void Warn(string label, string detail)
    {
        _problems++;
        Console.WriteLine($"  warn  {label,-20} {detail}");
    }

    private static void Bad(string label, string detail)
    {
        _problems++;
        Console.WriteLine($"  FAIL  {label,-20} {detail}");
    }
}
