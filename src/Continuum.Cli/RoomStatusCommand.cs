using Continuum.Core.Contracts;

namespace Continuum.Cli;

/// <summary>
/// <c>continuum room &lt;room-id&gt;</c> — what is actually happening in a room, and why an agent is silent.
///
/// <para>
/// This exists because "the room doesn't work" was unanswerable without reading the daemon's stdout,
/// the relay's per-session logs, the relay state directory and the message table. Being a member of a
/// room and *listening* to it are different things, and nothing showed the difference: an agent on the
/// manual channel_read pattern looks identical to a joined one until you notice it never replies.
/// </para>
/// <para>
/// Every line here answers a question that cost real debugging time: is the room open, who is bound to
/// it, how far behind is each of them, and when did each last do anything.
/// </para>
/// </summary>
public static class RoomStatusCommand
{
    /// <summary>A relay that has not run in this long is treated as asleep rather than waiting.</summary>
    private static readonly TimeSpan Stale = TimeSpan.FromMinutes(12);

    public static async Task<int> RunAsync(string[] args, CancellationToken ct)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Usage: continuum room <room-id> [--messages N]");
            Console.Error.WriteLine("Run `continuum rooms` to list ids.");
            return 1;
        }

        if (!Guid.TryParse(args[0], out var roomId))
        {
            Console.Error.WriteLine($"Not a room id: {args[0]}");
            return 1;
        }

        var show = 8;
        var i = Array.IndexOf(args, "--messages");
        if (i >= 0 && args.Length > i + 1 && int.TryParse(args[i + 1], out var n)) show = Math.Clamp(n, 1, 100);

        var cfg = Config.Load();
        if (cfg is null)
        {
            Console.Error.WriteLine("Continuum is not configured. Run `continuum doctor`.");
            return 1;
        }

        using var api = new Api(cfg);
        RoomDetailDto? detail;
        try { detail = await api.GetAsync<RoomDetailDto>($"/api/rooms/{roomId}?take=200", ct); }
        catch (ApiException e) when (e.Status == System.Net.HttpStatusCode.NotFound)
        {
            Console.Error.WriteLine("No such room, or it is not visible to this token.");
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Could not reach {cfg.Backend}: {ex.Message}");
            return 1;
        }
        if (detail is null) { Console.Error.WriteLine("Empty response from the backend."); return 1; }

        var room = detail.Room;
        var msgs = detail.Messages;
        var lastId = msgs.Count > 0 ? msgs[^1].Id : 0;
        var (listeners, unattributed) = LoadListeners(roomId);

        Console.WriteLine($"{room.Name}");
        Console.WriteLine(new string('─', Math.Min(room.Name.Length, 60)));
        Console.WriteLine($"  status     {room.Status}{(room.Status == "open" ? "" : "   — reopen it in the UI, or POST /api/rooms/{id}/reopen")}");
        Console.WriteLine($"  messages   {room.MessageCount}");
        Console.WriteLine($"  channel    #{room.ChannelName}");
        Console.WriteLine();

        // The heart of it: member ≠ listening.
        Console.WriteLine("Agents");
        if (detail.Members.Count == 0)
            Console.WriteLine("  (no members — nobody has joined)");

        foreach (var m in detail.Members.OrderBy(m => m.Agent))
        {
            if (!listeners.TryGetValue(m.Agent, out var l))
            {
                Console.WriteLine(unattributed > 0
                    ? $"  {m.Agent,-22} UNKNOWN — no relay identifies itself as this agent."
                    : $"  {m.Agent,-22} NOT LISTENING — no relay is bound to this agent.");
                Console.WriteLine($"  {"",-22} It will not see anything posted here. In its repo, run:");
                Console.WriteLine($"  {"",-22}   /continuum-joinroom {roomId} {m.Agent}");
                continue;
            }

            var behind = Math.Max(0, lastId - l.LastSeenId);
            var idle = DateTimeOffset.UtcNow - l.LastActive;
            var asleep = idle > Stale;

            var verdict = asleep
                ? $"ASLEEP — nothing for {Format(idle)}"
                : behind > 0 ? $"listening, {behind} message(s) behind" : "listening, up to date";

            Console.WriteLine($"  {m.Agent,-22} {verdict}");
            if (asleep)
                Console.WriteLine($"  {"",-22} Its session has stopped relaying. Type anything in that session to wake it.");
            else if (l.KeepAlives > 0)
                Console.WriteLine($"  {"",-22} waiting through a quiet room (keep-alive {l.KeepAlives})");
        }

        // An agent posting here without a relay is on the manual channel_read pattern — worth naming,
        // because it reads only on its own turns and will miss anything addressed to it.
        var speakers = msgs.Select(x => x.FromAgent).Distinct(StringComparer.OrdinalIgnoreCase);
        var memberNames = detail.Members.Select(x => x.Agent).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var strangers = speakers.Where(x => !memberNames.Contains(x)).ToList();
        if (strangers.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine($"Also posted here, but not members: {string.Join(", ", strangers)}");
            Console.WriteLine("  (a human posting from the web UI shows up here — that is normal)");
        }

        Console.WriteLine();
        Console.WriteLine($"Last {Math.Min(show, msgs.Count)} of {msgs.Count} message(s)");
        foreach (var m in msgs.TakeLast(show))
        {
            var when = m.CreatedAt.ToLocalTime().ToString("MM-dd HH:mm");
            Console.WriteLine($"  #{m.Id,-5} {when}  {m.FromAgent,-22} {OneLine(m.Body, 88)}");
        }

        // The question people actually arrive with.
        Console.WriteLine();
        if (unattributed > 0)
        {
            Console.WriteLine($"Note: {unattributed} relay session(s) predate this command and do not record which");
            Console.WriteLine("agent they serve, so an agent above may in fact be listening. They identify");
            Console.WriteLine("themselves on their next relay cycle — post a message, or wake the session.");
            Console.WriteLine();
        }

        var notListening = detail.Members.Count(m => !listeners.ContainsKey(m.Agent));
        var sleeping = listeners.Values.Count(l => DateTimeOffset.UtcNow - l.LastActive > Stale);
        if (room.Status != "open")
            Console.WriteLine("Nothing will move: the room is closed.");
        else if (notListening > 0 || sleeping > 0)
            Console.WriteLine($"Nothing will reach {notListening + sleeping} of {detail.Members.Count} agent(s) — see above.");
        else
            Console.WriteLine("Every member is listening. A message posted here will arrive as their next prompt.");

        return 0;
    }

    private sealed record Listener(long LastSeenId, int KeepAlives, DateTimeOffset LastActive);

    /// <summary>
    /// Which agents have a relay bound to this room, from the per-session state files. The file's own
    /// write time is the freshness signal — the relay saves state every cycle, so a state file that
    /// stopped changing means a session that stopped relaying.
    /// </summary>
    private static (Dictionary<string, Listener> Found, int Unattributed) LoadListeners(Guid roomId)
    {
        var found = new Dictionary<string, Listener>(StringComparer.OrdinalIgnoreCase);
        var unattributed = 0;
        if (!Directory.Exists(Config.StateDir)) return (found, unattributed);

        foreach (var path in Directory.EnumerateFiles(Config.StateDir, "*.json"))
        {
            RelayState state;
            try { state = RelayState.Load(path); }
            catch { continue; }

            // Written by a relay older than these fields: it may well be bound to this room, and
            // claiming otherwise would make this command lie. Count it and say so instead.
            if (state.Agent is null || state.RoomId is null) { unattributed++; continue; }
            if (!Guid.TryParse(state.RoomId, out var bound) || bound != roomId) continue;

            var active = new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero);

            // Two sessions can claim one agent name; the more recent one is the live relay.
            if (found.TryGetValue(state.Agent, out var seen) && seen.LastActive >= active) continue;
            found[state.Agent] = new Listener(state.LastSeenId, state.KeepAlives, active);
        }
        return (found, unattributed);
    }

    private static string Format(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours}h {t.Minutes}m" : $"{(int)t.TotalMinutes}m";

    private static string OneLine(string? s, int max)
    {
        var t = (s ?? "").Replace('\n', ' ').Replace('\r', ' ').Trim();
        while (t.Contains("  ")) t = t.Replace("  ", " ");
        return t.Length <= max ? t : string.Concat(t.AsSpan(0, max - 1), "…");
    }
}
