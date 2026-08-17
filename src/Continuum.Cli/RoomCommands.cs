using System.Text.Json;
using Continuum.Core.Contracts;

namespace Continuum.Cli;

/// <summary>
/// <c>continuum join</c> / <c>continuum leave</c> — ports of room-join.ps1 / room-leave.ps1.
///
/// Everything written here lands in the session transcript, because these run as a slash command's
/// output. That makes join the one and only place the room's system prompt is delivered; per-turn
/// messages from the relay stay raw.
/// </summary>
public static class RoomCommands
{
    public static async Task<int> JoinAsync(string[] args, CancellationToken ct)
    {
        // The slash command passes "<ROOM_ID> <AGENT_NAME>" as a single $ARGUMENTS string, so a
        // one-argument invocation carrying both is normal, not a mistake.
        var parts = args.SelectMany(a => a.Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToArray();
        if (parts.Length < 2)
        {
            Console.WriteLine("Usage: continuum join <ROOM_ID> <AGENT_NAME>");
            return 1;
        }

        var roomArg = parts[0];
        var agent = parts[1];

        var cfg = Config.Load();
        if (cfg is null)
        {
            Console.WriteLine("Continuum is not configured on this machine. Run `continuum doctor` to see what's missing.");
            return 1;
        }

        if (!Guid.TryParse(roomArg, out var roomId))
        {
            Console.WriteLine($"'{roomArg}' is not a room id. Copy the id from the room page in the web UI.");
            return 1;
        }

        using var api = new Api(cfg);
        RoomDto? room;
        try
        {
            var rooms = await api.GetAsync<List<RoomDto>>("/api/rooms", ct);
            room = rooms?.FirstOrDefault(r => r.Id == roomId);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Could not reach {cfg.Backend}: {ex.Message}");
            return 1;
        }

        if (room is null)
        {
            Console.WriteLine($"No room with id '{roomId}' is visible to this token.");
            return 1;
        }
        if (room.Status != "open")
        {
            Console.WriteLine($"Room \"{room.Name}\" is {room.Status} — cannot join.");
            return 1;
        }

        // Best effort: the relay still works if this 403s, since posting is what actually matters.
        await api.TryPostAsync($"/api/rooms/{roomId}/members", new AddMemberRequest(agent), ct);

        Console.WriteLine($"You have joined Continuum room \"{room.Name}\" as agent \"{agent}\".");
        Console.WriteLine();

        if (!string.IsNullOrWhiteSpace(room.SystemPrompt))
        {
            Console.WriteLine("===== ROOM SYSTEM PROMPT — your standing instructions for this room =====");
            Console.WriteLine(room.SystemPrompt);
            Console.WriteLine("========================================================================");
            Console.WriteLine();
        }
        if (!string.IsNullOrWhiteSpace(room.Topic))
        {
            Console.WriteLine($"Goal / topic: {room.Topic}");
            Console.WriteLine();
        }

        Console.WriteLine(
            "HOW THE ROOM WORKS: after each reply you write, your message is sent to the room automatically, "
            + "and the other participant's next message is delivered back to you as your next prompt — a live "
            + "back-and-forth, no copy-paste. Keep every turn short and ACTION-oriented: change code, run the "
            + "test, report the result — do not just discuss. When the goal is met and verified, begin a message "
            + "with [DONE] to end the room.");
        Console.WriteLine();
        Console.WriteLine(
            "If you are the initiator: state your concrete goal and take your first action now. "
            + "If you joined to respond: reply with exactly the word  ready  and wait.");
        Console.WriteLine();

        // The relay is a Stop hook registered per repo. Without it, join succeeds and then nothing
        // ever happens — the silent failure this warning exists to prevent.
        if (!StopHookInstalled(Directory.GetCurrentDirectory()))
        {
            Console.WriteLine(
                "NOTE: the relay Stop hook is not registered in this folder, so your replies will NOT be sent. "
                + "Run `continuum setup-relay` here, then restart the session and join again.");
            Console.WriteLine();
        }

        Console.WriteLine("(system marker for the relay — ignore this line)");
        Console.WriteLine($"<<CONTINUUM-ROOM room={roomId} agent={agent} channel={room.ChannelName}>>");
        return 0;
    }

    public static int Leave()
    {
        Console.WriteLine("Left the room. Automatic relay is now OFF for this session (your replies are no longer posted).");
        Console.WriteLine("(system marker for the relay — ignore this line)");
        Console.WriteLine("<<CONTINUUM-ROOM-LEAVE>>");
        return 0;
    }

    /// <summary>True when some Stop hook in this folder's settings runs the relay.</summary>
    internal static bool StopHookInstalled(string dir)
    {
        foreach (var name in new[] { "settings.local.json", "settings.json" })
        {
            var path = Path.Combine(dir, ".claude", name);
            if (!File.Exists(path)) continue;
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                if (!doc.RootElement.TryGetProperty("hooks", out var hooks)) continue;
                if (!hooks.TryGetProperty("Stop", out var stop) || stop.ValueKind != JsonValueKind.Array) continue;

                foreach (var group in stop.EnumerateArray())
                {
                    if (!group.TryGetProperty("hooks", out var inner) || inner.ValueKind != JsonValueKind.Array) continue;
                    foreach (var h in inner.EnumerateArray())
                        // `room-relay` is the retired PowerShell hook: a Windows machine that
                        // installed it still relays, so don't tell that user they're unconfigured.
                        if (h.TryGetProperty("command", out var c) && c.GetString() is { } cmd
                            && (cmd.Contains("relay-turn") || cmd.Contains("room-relay")))
                            return true;
                }
            }
            catch (JsonException) { /* a malformed settings file isn't ours to fix here */ }
        }
        return false;
    }
}
