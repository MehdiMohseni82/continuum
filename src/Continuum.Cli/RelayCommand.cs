using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Continuum.Core.Contracts;
using Continuum.Core.Rooms;

namespace Continuum.Cli;

/// <summary>
/// The Claude Code <c>Stop</c> hook: a port of hooks/relay/room-relay.ps1.
///
/// When a session finishes a turn this posts its last message to the room, waits for the peer, and
/// hands the peer's message back as the session's next prompt by answering
/// <c>{"decision":"block"}</c>. That block protocol is the whole "no copy-paste" trick.
///
/// STDOUT DISCIPLINE: Claude Code parses stdout as JSON, so nothing is written there except the
/// final decision object. Diagnostics go to ~/.continuum/relay/log/&lt;session&gt;.log.
///
/// Fail-open throughout: any error lets the session stop normally rather than wedging it.
/// </summary>
public static partial class RelayCommand
{
    private const int PollWindowSeconds = 560;
    private const int NoProgressTurnLimit = 4;

    /// <summary>
    /// How many times to re-arm the wait when the room is quiet. The poll window cannot simply be made
    /// longer: it is bounded by the Stop hook's own timeout (600s in the settings that register it), and
    /// a hook that overruns is killed. So on expiry the relay hands the session a turn that answers
    /// PASS, which ends, which fires the Stop hook again, which waits afresh.
    ///
    /// Four cycles — about thirty-seven minutes of silence — then the session is handed back. The
    /// original forty (six hours) was chosen when the relay was the only way an agent could be
    /// reached; the room runner now covers a room whose participants have gone home, so holding
    /// someone's terminal hostage all day buys nothing. A blocking Stop hook is intrusive by nature:
    /// it should be spent on delivering messages, not on waiting for them.
    /// </summary>
    private const int KeepAliveCycles = 4;
    private const int LargeMessageChars = 8000;

    /// <summary>A turn that showed work: a code block, or the vocabulary of a test result.</summary>
    [GeneratedRegex(@"(?i)\b(passed|failed|tests?\s+run|assert|traceback|error:)\b")]
    private static partial Regex ShowedWork();

    public static async Task<int> RunAsync(CancellationToken ct)
    {
        var raw = await Console.In.ReadToEndAsync(ct);
        var input = HookInput.Parse(raw);

        var sessionId = string.IsNullOrWhiteSpace(input?.SessionId) ? "unknown" : input!.SessionId!;
        Directory.CreateDirectory(Config.LogDir);
        Directory.CreateDirectory(Config.StateDir);
        var logPath = Path.Combine(Config.LogDir, $"{sessionId}.log");

        void Log(string m)
        {
            try { File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss}] {m}\n"); } catch { }
        }

        try
        {
            var transcript = input?.TranscriptPath;
            if (string.IsNullOrWhiteSpace(transcript) || !File.Exists(transcript))
            {
                // Abnormal: a Stop hook always gets a transcript. Worth a line, unlike the common
                // "this session simply isn't in a room" case below, which must stay silent.
                Log($"no readable transcript in the hook payload ({raw.Length} bytes on stdin) — nothing to relay");
                return 0;
            }

            // 1. Is this session bound to a room? Read the marker `continuum join` printed.
            var text = await File.ReadAllTextAsync(transcript, ct);
            if (Transcript.CurrentBind(text) is not { } bind) return 0; // not in a room — a pure no-op

            var roomId = bind.RoomId;
            var agent = bind.Agent;

            // Never drive an agent the daemon's room runner is already driving. Both at once is the
            // worst of both: the runner posts the agent's messages, so the relay never has anything
            // to deliver and does nothing but fire keep-alives — which seize the human's session
            // every few minutes, forever, for no benefit. Seen for real: eight PASS cycles in a row
            // in a session someone was trying to work in.
            if (RunnerAgents.Load().Any(a => string.Equals(a.Name, agent, StringComparison.OrdinalIgnoreCase)))
            {
                Log($"'{agent}' is driven by the room runner; leaving the room to it and not relaying");
                return 0;
            }

            var cfg = Config.Load();
            if (cfg is null) { Log("not configured — run `continuum doctor`"); return 0; }

            using var api = new Api(cfg, TimeSpan.FromSeconds(25));
            var statePath = Path.Combine(Config.StateDir, $"{sessionId}.json");
            var state = RelayState.Load(statePath);
            state.Agent = agent;
            state.RoomId = roomId.ToString();

            // 2. Still open? Closing the room in the UI is the documented force-stop.
            var room = await FindRoomAsync(api, roomId, ct);
            if (room is null) { Log("could not reach the room (backend error, not a closed room); stopping"); return 0; }
            if (room.Status != "open") { Log($"room is {room.Status}; stopping"); return 0; }

            // 3. My last spoken message, with the token usage of the turn that produced it.
            var (mine, usage) = Transcript.LastAssistantMessage(transcript);

            // PASS is the shared silence sentinel (RoomTurn, so daemon and relay agree); `ready` is
            // the relay's own join handshake. Neither is content, so neither goes to the room.
            //
            // A keep-alive turn is also never posted, and that is decided by what we asked for rather
            // than by what came back: IsPass demands the message be exactly "PASS", so a model that
            // replies "PASS." or "Okay — PASS" would otherwise have its filler posted into the room
            // every nine minutes. We know we asked for silence; honour that whatever it says.
            var awaitingKeepAlive = state.AwaitingKeepAlive;
            var skipPost = string.IsNullOrWhiteSpace(mine)
                           || awaitingKeepAlive
                           || RoomTurn.IsPass(mine)
                           || Transcript.IsReadyHandshake(mine);

            if (!skipPost && mine != state.LastPosted)
            {
                try
                {
                    var posted = await api.PostAsync<MessageDto>($"/api/rooms/{roomId}/post",
                        new RoomPostRequest(agent, mine!,
                            usage?.InputTokens, usage?.OutputTokens,
                            usage?.CacheReadInputTokens, usage?.CacheCreationInputTokens), ct);

                    state.LastPosted = mine;
                    if (posted is not null) state.LastSeenId = Math.Max(state.LastSeenId, posted.Id);
                    Log($"posted ({mine!.Length} chars)");
                }
                catch (Exception ex) { Log($"post failed: {ex.Message}"); state.Save(statePath); return 0; }

                if (RoomTurn.IsDone(mine)) { Log("I declared [DONE]; stopping"); state.Save(statePath); return 0; }
            }

            // 4. Act-not-talk guard, for repo-backed agents only.
            //
            // Skipped for a keep-alive turn. The guard counts turns that discuss without changing
            // anything, and a PASS we ourselves asked for is neither: left in, four cycles of quiet
            // (~37 minutes) would trip it and post [DONE] into a room nobody had abandoned.
            var wasKeepAlive = awaitingKeepAlive;
            state.AwaitingKeepAlive = false;

            var cwd = input?.Cwd;
            if (!wasKeepAlive && !string.IsNullOrWhiteSpace(cwd) && Directory.Exists(Path.Combine(cwd!, ".git")))
            {
                var tree = WorkingTreeSignature(cwd!);
                var showedWork = mine is not null && (mine.Contains("```") || ShowedWork().IsMatch(mine));

                if (state.LastTree is not null && tree == state.LastTree && !showedWork) state.TalkTurns++;
                else state.TalkTurns = 0;
                state.LastTree = tree;

                if (state.TalkTurns >= NoProgressTurnLimit)
                {
                    Log($"no-progress guard tripped ({state.TalkTurns} talk turns)");
                    try
                    {
                        await api.PostAsync<MessageDto>($"/api/rooms/{roomId}/post", new RoomPostRequest(agent,
                            "[DONE] Stopping: 4 turns of discussion with no code change and no test run. "
                            + "This needs a concrete action or a human decision, not more analysis."), ct);
                    }
                    catch { }
                    state.Save(statePath);
                    return 0;
                }
            }

            // 5. Wait for the peer, then hand their message back as this session's next prompt.
            state.Save(statePath);
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed.TotalSeconds < PollWindowSeconds)
            {
                ct.ThrowIfCancellationRequested();

                List<MessageDto>? incoming = null;
                try
                {
                    incoming = await api.GetAsync<List<MessageDto>>(
                        $"/api/rooms/{roomId}/messages?since={state.LastSeenId}&take=50", ct);
                }
                catch (Exception ex) { Log($"poll failed: {ex.Message}"); }

                foreach (var m in incoming ?? [])
                {
                    if (m.Id > state.LastSeenId) state.LastSeenId = m.Id;
                    if (m.FromAgent == agent) continue; // skip our own echo

                    // A real message means the room is alive again: start the silence count over.
                    state.KeepAlives = 0;
                    state.AwaitingKeepAlive = false;
                    state.Save(statePath);
                    if (RoomTurn.IsDone(m.Body)) { Log($"peer {m.FromAgent} declared [DONE]; stopping"); return 0; }

                    string reason;
                    if (m.Body.Length > LargeMessageChars)
                    {
                        // Too big to inline as a prompt; hand over a path instead.
                        var inbox = Path.Combine(Config.StateDir, $"{sessionId}.incoming.txt");
                        await File.WriteAllTextAsync(inbox, m.Body, ct);
                        reason = $"New room message from {m.FromAgent} (large). Read it from: {inbox} — then respond with your next action.";
                    }
                    else
                    {
                        reason = $"Room message from {m.FromAgent} — your turn to respond:\n\n{m.Body}";
                    }

                    Log($"delivering peer msg id={m.Id} from {m.FromAgent}");
                    Console.Out.Write(JsonSerializer.Serialize(new { decision = "block", reason }));
                    return 0;
                }

                if (incoming is { Count: > 0 }) state.Save(statePath);

                // The room can be force-stopped while we wait.
                var still = await FindRoomAsync(api, roomId, ct);
                if (still is null) { Log("lost contact with the backend while waiting; stopping"); return 0; }
                if (still.Status != "open") { Log($"room became {still.Status} while waiting; stopping"); return 0; }

                await Task.Delay(TimeSpan.FromSeconds(2), ct);
            }

            // The room is quiet. Going idle here is what made an agent unreachable: the only delivery
            // path is this Stop hook, and a session that has stopped ends no more turns, so a message
            // written afterwards was never seen by anyone. Re-arm instead — hand back a turn that says
            // PASS, which posts nothing (RoomTurn.IsPass) and brings us straight back here.
            if (state.KeepAlives < KeepAliveCycles)
            {
                state.KeepAlives++;
                state.AwaitingKeepAlive = true;
                state.Save(statePath);
                Log($"poll window elapsed; re-arming (keep-alive {state.KeepAlives}/{KeepAliveCycles})");

                Console.Out.Write(JsonSerializer.Serialize(new
                {
                    decision = "block",
                    reason = "[continuum] Still connected to the room; nothing new was said. "
                             + "Do not act, do not summarise, do not post. Reply with exactly: PASS",
                }));
                return 0;
            }

            Log($"room quiet for {state.KeepAlives} cycles; releasing this session "
                + "(rejoin with /continuum-joinroom, or let the room runner drive this agent)");
            state.Save(statePath);
            return 0;
        }
        catch (Exception ex)
        {
            Log($"relay error (fail-open): {ex.Message}");
            return 0;
        }
    }

    internal static async Task<RoomDto?> FindRoomAsync(Api api, Guid roomId, CancellationToken ct)
    {
        try
        {
            var rooms = await api.GetAsync<List<RoomDto>>("/api/rooms", ct);
            return rooms?.FirstOrDefault(r => r.Id == roomId);
        }
        catch { return null; }
    }

    /// <summary>
    /// A signature of the working tree: the changed-file list PLUS the actual diff, so repeated
    /// edits to one file register as progress. Porcelain alone only lists names.
    /// </summary>
    private static string WorkingTreeSignature(string cwd)
    {
        var porcelain = Git(cwd, "status --porcelain");
        var diff = Git(cwd, "diff HEAD");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(porcelain + "\n---\n" + diff)));
    }

    private static string Git(string cwd, string args)
    {
        try
        {
            var psi = new ProcessStartInfo("git")
            {
                WorkingDirectory = cwd,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var a in args.Split(' ', StringSplitOptions.RemoveEmptyEntries)) psi.ArgumentList.Add(a);

            using var p = Process.Start(psi);
            if (p is null) return "";
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(10_000);
            return output;
        }
        catch { return ""; }
    }
}

/// <summary>Per-session relay bookkeeping, so a restarted turn doesn't re-deliver history.</summary>
public sealed class RelayState
{
    public long LastSeenId { get; set; }
    public string? LastPosted { get; set; }
    public string? LastTree { get; set; }
    public int TalkTurns { get; set; }

    /// <summary>Consecutive keep-alive cycles with no peer message. Reset by any real message.</summary>
    public int KeepAlives { get; set; }

    /// <summary>True when the previous cycle asked for a PASS purely to stay connected.</summary>
    public bool AwaitingKeepAlive { get; set; }

    /// <summary>Which agent and room this session is bound to. Recorded so `continuum room` can say
    /// who is actually listening without reading every session transcript.</summary>
    public string? Agent { get; set; }

    public string? RoomId { get; set; }

    public static RelayState Load(string path)
    {
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<RelayState>(File.ReadAllText(path)) ?? new RelayState();
        }
        catch { }
        return new RelayState();
    }

    public void Save(string path)
    {
        try { File.WriteAllText(path, JsonSerializer.Serialize(this)); } catch { }
    }
}
