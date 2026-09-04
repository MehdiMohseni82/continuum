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
            Console.Error.WriteLine("Usage: continuum room <room-id> [--messages N] [--follow]");
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
        var follow = args.Contains("--follow") || args.Contains("-f");

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
        var driven = RunnerAgents.Load().ToDictionary(a => a.Name, StringComparer.OrdinalIgnoreCase);

        // When did each agent last actually speak? A turn that ran after an agent's last message and
        // produced nothing is the agent choosing to stay quiet — which is the common case once every
        // open item is waiting on a person, and it looks exactly like a broken room.
        var lastSpoke = msgs
            .GroupBy(x => x.FromAgent, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Max(x => x.CreatedAt), StringComparer.OrdinalIgnoreCase);

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

        var quietTurns = 0;
        foreach (var m in detail.Members.OrderBy(m => m.Agent))
        {
            // The runner drives an agent with no session of its own, so a relay-only view reported
            // three working agents as "not listening" while they were posting every cycle.
            if (driven.TryGetValue(m.Agent, out var d))
            {
                var mode = d.Write ? "runner-driven, may edit" : "runner-driven, read-only";
                if (d.LastTurn is not { } turn)
                {
                    Console.WriteLine($"  {m.Agent,-22} {mode} — no turn taken yet");
                    continue;
                }

                // The runner records the outcome, so this is read rather than inferred; timestamps
                // were ambiguous whenever a turn and its message landed in the same minute.
                var outcome = RunnerAgents.LastOutcome(m.Agent);
                var spoke = lastSpoke.TryGetValue(m.Agent, out var t) ? t : (DateTimeOffset?)null;

                var what = outcome switch
                {
                    "posted" => spoke is { } p ? $"posted {p.ToLocalTime():HH:mm}" : "posted",
                    "no-post" => "chose not to post",
                    "unknown" => "outcome not confirmed",
                    _ => spoke is { } p2 ? $"last spoke {p2.ToLocalTime():HH:mm}" : "has not spoken",
                };
                if (outcome == "no-post") quietTurns++;

                Console.WriteLine($"  {m.Agent,-22} {mode} — last turn {turn.ToLocalTime():HH:mm}, {what}");

                if (RunnerAgents.LastTurnNote(m.Agent) is { } note)
                    Console.WriteLine($"  {"",-22} \"{note}\"");
                continue;
            }

            if (!listeners.TryGetValue(m.Agent, out var l))
            {
                Console.WriteLine(unattributed > 0
                    ? $"  {m.Agent,-22} UNKNOWN — not runner-driven, and no relay identifies itself as this agent."
                    : $"  {m.Agent,-22} NOT LISTENING — not runner-driven, and no relay is bound to it.");
                Console.WriteLine($"  {"",-22} It will not see anything posted here. Either add it to");
                Console.WriteLine($"  {"",-22} ~/Continuum/rooms/agents.json, or in its repo run:");
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
        var unresolved = detail.Members.Any(m =>
            !driven.ContainsKey(m.Agent) && !listeners.ContainsKey(m.Agent));
        if (unattributed > 0 && unresolved)
        {
            Console.WriteLine($"Note: {unattributed} relay session(s) predate this command and do not record which");
            Console.WriteLine("agent they serve, so an agent above may in fact be listening. They identify");
            Console.WriteLine("themselves on their next relay cycle — post a message, or wake the session.");
            Console.WriteLine();
        }

        var unreachable = detail.Members.Count(m =>
            !driven.ContainsKey(m.Agent) && !listeners.ContainsKey(m.Agent));
        var sleeping = listeners.Values.Count(l => DateTimeOffset.UtcNow - l.LastActive > Stale);

        if (room.Status != "open")
            Console.WriteLine("Nothing will move: the room is closed.");
        else if (unreachable > 0 || sleeping > 0)
            Console.WriteLine($"Nothing will reach {unreachable + sleeping} of {detail.Members.Count} agent(s) — see above.");
        else if (quietTurns == detail.Members.Count && detail.Members.Count > 0)
        {
            // The state that reads as a dead room but is not one.
            Console.WriteLine("Every agent took a turn and chose not to post. The room is not stuck —");
            Console.WriteLine("it is waiting on a person. Read the quotes above: they usually name what for.");
        }
        else
            Console.WriteLine("Every member is reachable. A message posted here reaches them on their next turn.");

        if (follow) return await FollowAsync(api, roomId, lastId, ct);
        return 0;
    }

    /// <summary>
    /// Tail the room. This is the "watch them work" view: the runner drives agents headlessly, so
    /// nothing appears in any interactive terminal unless something prints it.
    /// </summary>
    private static async Task<int> FollowAsync(Api api, Guid roomId, long since, CancellationToken ct)
    {
        Console.WriteLine();
        Console.WriteLine("Following. Ctrl-C to stop.");

        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(TimeSpan.FromSeconds(5), ct); }
            catch (OperationCanceledException) { break; }

            List<MessageDto>? incoming;
            try
            {
                incoming = await api.GetAsync<List<MessageDto>>(
                    $"/api/rooms/{roomId}/messages?since={since}&take=50", ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A blip must not end the watch — say so and keep going.
                Console.WriteLine($"  (lost contact: {ex.Message})");
                continue;
            }

            foreach (var m in incoming ?? [])
            {
                since = Math.Max(since, m.Id);
                Console.WriteLine();
                Console.WriteLine($"#{m.Id}  {m.CreatedAt.ToLocalTime():HH:mm:ss}  {m.FromAgent}");
                foreach (var line in m.Body.Split('\n'))
                    Console.WriteLine($"    {line}");
            }
        }
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
