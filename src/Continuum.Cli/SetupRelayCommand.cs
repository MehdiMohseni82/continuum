using System.Text.Json;
using System.Text.Json.Nodes;

namespace Continuum.Cli;

/// <summary>
/// <c>continuum setup-relay [dir]</c> — the cross-platform replacement for install-room-relay.ps1.
///
/// Two jobs: register the Stop hook in ONE folder (never machine-wide — a relay that runs on every
/// session in every repo would post stray messages into rooms), and write the two slash commands.
///
/// The commands are path-free: they invoke <c>continuum</c> from PATH. The old ones baked in an
/// absolute path, so Claude settings sync carried a <c>C:/Users/...</c> command onto this Mac where
/// it was visible, invocable and dead.
/// </summary>
public static class SetupRelayCommand
{
    private const string JoinCommand = """
        ---
        description: Join a Continuum room and start the automatic relay for this session
        argument-hint: <ROOM_ID> <AGENT_NAME>
        allowed-tools: Bash(continuum:*)
        ---
        !`continuum join $ARGUMENTS`

        Read the output above and follow it — that framing is your standing instruction set for this room.

        """;

    private const string LeaveCommand = """
        ---
        description: Leave the current Continuum room (stop the auto-relay for this session)
        allowed-tools: Bash(continuum:*)
        ---
        !`continuum leave`

        """;

    public static int Run(string[] args)
    {
        var dir = args.Length > 0 ? Path.GetFullPath(args[0]) : Directory.GetCurrentDirectory();
        if (!Directory.Exists(dir))
        {
            Console.Error.WriteLine($"No such folder: {dir}");
            return 1;
        }

        var settingsPath = Path.Combine(dir, ".claude", "settings.local.json");
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);

        JsonObject settings;
        try
        {
            settings = File.Exists(settingsPath)
                ? JsonNode.Parse(File.ReadAllText(settingsPath)) as JsonObject ?? new JsonObject()
                : new JsonObject();
        }
        catch (JsonException ex)
        {
            // Refuse rather than overwrite: this file is the user's, and it holds their tool
            // permissions. install-agent.ps1's Add-Member -Force destroyed exactly this.
            Console.Error.WriteLine($"{settingsPath} is not valid JSON ({ex.Message}). Fix or remove it, then re-run.");
            return 1;
        }

        var hooks = settings["hooks"] as JsonObject ?? new JsonObject();
        settings["hooks"] = hooks;
        var stop = hooks["Stop"] as JsonArray ?? new JsonArray();
        hooks["Stop"] = stop;

        var already = stop.OfType<JsonObject>()
            .SelectMany(g => (g["hooks"] as JsonArray)?.OfType<JsonObject>() ?? Enumerable.Empty<JsonObject>())
            .Any(h => h["command"]?.GetValue<string>() is { } c && c.Contains("relay-turn"));

        if (!already)
        {
            stop.Add(new JsonObject
            {
                ["hooks"] = new JsonArray(new JsonObject
                {
                    ["type"] = "command",
                    ["command"] = "continuum relay-turn",
                    // The relay long-polls for up to 560s; the hook must outlive that.
                    ["timeout"] = 600,
                }),
            });
        }

        var env = settings["env"] as JsonObject ?? new JsonObject();
        settings["env"] = env;
        // A room is many consecutive hook-driven continuations with no human turn; the default cap
        // would end the conversation after a handful of exchanges.
        env["CLAUDE_CODE_STOP_HOOK_BLOCK_CAP"] = "100000";

        File.WriteAllText(settingsPath, settings.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        var cmdDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "commands");
        Directory.CreateDirectory(cmdDir);
        File.WriteAllText(Path.Combine(cmdDir, "continuum-joinroom.md"), JoinCommand);
        File.WriteAllText(Path.Combine(cmdDir, "continuum-leaveroom.md"), LeaveCommand);

        Console.WriteLine("Room relay installed.");
        Console.WriteLine($"  hook:     Stop → `continuum relay-turn`  in {settingsPath} (this folder only)");
        Console.WriteLine($"  commands: /continuum-joinroom, /continuum-leaveroom  in {cmdDir}");
        Console.WriteLine();
        Console.WriteLine("Restart the Claude session in this folder so the hook loads, then:");
        Console.WriteLine("  /continuum-joinroom <ROOM_ID> <AGENT_NAME>");
        return 0;
    }
}
