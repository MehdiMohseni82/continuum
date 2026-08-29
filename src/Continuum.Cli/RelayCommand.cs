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

            var cfg = Config.Load();
            if (cfg is null) { Log("not configured — run `continuum doctor`"); return 0; }

            using var api = new Api(cfg, TimeSpan.FromSeconds(25));
            var statePath = Path.Combine(Config.StateDir, $"{sessionId}.json");
            var state = RelayState.Load(statePath);

            // 2. Still open? Closing the room in the UI is the documented force-stop.
            var room = await FindRoomAsync(api, roomId, ct);
            if (room is null || room.Status != "open") { Log("room closed or unreachable; stopping"); return 0; }

            // 3. My last spoken message, with the token usage of the turn that produced it.
            var (mine, usage) = Transcript.LastAssistantMessage(transcript);

            // PASS is the shared silence sentinel (RoomTurn, so daemon and relay agree); `ready` is
            // the relay's own join handshake. Neither is content, so neither goes to the room.
            var skipPost = string.IsNullOrWhiteSpace(mine)
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
            var cwd = input?.Cwd;
            if (!string.IsNullOrWhiteSpace(cwd) && Directory.Exists(Path.Combine(cwd!, ".git")))
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
                if (still is null || still.Status != "open") { Log("room closed while waiting; stopping"); return 0; }

                await Task.Delay(TimeSpan.FromSeconds(2), ct);
            }

            Log("poll window elapsed with no peer message; idling");
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
