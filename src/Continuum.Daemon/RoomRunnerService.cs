using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Continuum.Core.Contracts;
using Continuum.Core.Domain;
using Continuum.Core.Rooms;
using Microsoft.Extensions.Options;

namespace Continuum.Daemon;

/// <summary>
/// Cross-platform successor to hooks/room-runner.ps1, hosted inside the daemon so it inherits the
/// daemon's auto-start + supervised restart (self fail-over). Each cycle it wakes each configured
/// local agent to take one turn in the open rooms it belongs to, so agents hold a live conversation.
///
/// Turn rule (unchanged): greet if the room is empty and you joined first; otherwise reply when the
/// latest message is from someone else — but if that message @mentions specific members, only they
/// answer. One turn per agent per cycle; a per-agent in-flight guard prevents overlapping runs.
/// </summary>
public sealed class RoomRunnerService(
    ILogger<RoomRunnerService> log,
    IOptions<DaemonOptions> options,
    BackendClient backend) : BackgroundService
{
    private readonly RoomRunnerOptions _opt = options.Value.RoomRunner;
    private readonly ConcurrentDictionary<string, Task> _inFlight = new();  // agent name → running turn
    private readonly ConcurrentDictionary<string, long> _lastActed = new(); // agent+room → tail msg id last acted on
    private readonly Dictionary<string, string?> _cli = new();              // runtime → resolved CLI path (cached)
    private readonly HashSet<string> _warnedMissing = new();                // runtimes we've already warned about

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!_opt.Enabled)
        {
            log.LogInformation("Room runner disabled (Daemon:RoomRunner:Enabled=false).");
            return;
        }

        Directory.CreateDirectory(_opt.LogDir);
        log.LogInformation("Room runner started (interval {Interval}s). Runtimes resolved per agent (claude/codex/cursor).",
            _opt.IntervalSeconds);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await RunCycleAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Room runner cycle failed");
            }

            try { await Task.Delay(TimeSpan.FromSeconds(_opt.IntervalSeconds), ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task RunCycleAsync(CancellationToken ct)
    {
        var agents = LoadAgents();
        if (agents.Count == 0) return;

        List<RoomDto> openRooms;
        try
        {
            openRooms = [.. (await backend.GetRoomsAsync(ct)).Where(r => r.Status == "open")];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogWarning("Room runner: rooms fetch failed: {Msg}", ex.Message);
            return;
        }
        if (openRooms.Count == 0) return;

        foreach (var room in openRooms)
        {
            RoomDetailDto? detail;
            try { detail = await backend.GetRoomAsync(room.Id, ct); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogDebug("Room {Room} detail fetch failed: {Msg}", room.Id, ex.Message);
                continue;
            }
            if (detail is null) continue;

            var memberNames = detail.Members.Select(m => m.Agent).ToList();
            var memberSet = memberNames.ToHashSet();
            var msgs = detail.Messages;
            var recent = msgs.Select(m => (m.FromAgent, m.Body)).ToList();
            var lastId = msgs.Count > 0 ? msgs[^1].Id : 0L;
            var streak = RoomTurn.TrailingAgentStreak(msgs.Select(m => m.FromAgent).ToList(), memberSet);

            // Room-level terminal conditions (agent-independent): an explicit [DONE], or the autonomous-turn
            // cap reached with no human in the loop. Close the room instead of driving it further.
            var lastBody = msgs.Count > 0 ? msgs[^1].Body : null;
            if (RoomTurn.IsDone(lastBody)) { await CloseRoomAsync(room, "an agent declared it done", ct); continue; }
            if (_opt.MaxAutonomousTurns > 0 && streak >= _opt.MaxAutonomousTurns)
            {
                await CloseRoomAsync(room, $"autonomous-turn cap ({_opt.MaxAutonomousTurns}) reached without a conclusion", ct);
                continue;
            }

            foreach (var agent in agents)
            {
                if (!memberSet.Contains(agent.Name)) continue; // not a member here

                // Skip if this agent is still mid-turn from a previous cycle (one turn per agent per cycle).
                if (_inFlight.TryGetValue(agent.Name, out var running))
                {
                    if (!running.IsCompleted) continue;
                    _inFlight.TryRemove(agent.Name, out _);
                }

                var decision = RoomTurn.Decide(agent.Name, memberNames, recent, streak, _opt.MaxAutonomousTurns);
                if (!decision.IsTurn) continue;

                // Give each agent one attempt per tail-message state, so an agent that chooses to stay silent
                // (posts nothing) isn't re-spawned every cycle until someone else has posted.
                var key = agent.Name + " " + room.Id;
                if (_lastActed.TryGetValue(key, out var acted) && acted == lastId) continue;

                var cli = ResolveRuntime(agent.Runtime);
                if (cli is null)
                {
                    if (_warnedMissing.Add(agent.Runtime.ToLowerInvariant()))
                        log.LogWarning("Room runner: '{Runtime}' CLI not found on PATH/~/.local/bin — agents using it are skipped "
                                     + "until it's installed and logged in.", agent.Runtime);
                    continue; // this agent can't act; try the next member
                }

                _lastActed[key] = lastId;
                var prompt = BuildPrompt(agent, room, memberNames, msgs);
                log.LogInformation("Waking '{Agent}' ({Runtime}) in '{Room}' → {Why}", agent.Name, agent.Runtime, room.Name, decision.Why);
                _inFlight[agent.Name] = RunTurnAsync(agent, cli, prompt, ct);
            }
        }
    }

    /// <summary>Best-effort close of a room the runner has decided is finished. Logs the reason; a failed
    /// close just means the room is retried (and closed) next cycle — it is never left running.</summary>
    private async Task CloseRoomAsync(RoomDto room, string reason, CancellationToken ct)
    {
        try
        {
            if (await backend.CloseRoomAsync(room.Id, ct))
                log.LogInformation("Room runner: closed '{Room}' — {Reason}.", room.Name, reason);
            else
                log.LogWarning("Room runner: could not close '{Room}' ({Reason}); will retry next cycle.", room.Name, reason);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogWarning("Room runner: close '{Room}' failed: {Msg}", room.Name, ex.Message);
        }
    }

    private string BuildPrompt(LocalAgent agent, RoomDto room, List<string> memberNames, IReadOnlyList<MessageDto> msgs)
    {
        var name = agent.Name;
        // Humans = anyone who has spoken but isn't a member agent (the room owner joining in).
        var humans = msgs.Select(m => m.FromAgent).Distinct()
                         .Where(f => !memberNames.Contains(f)).ToHashSet();

        var last = msgs.Count > 0 ? msgs[^1] : null;
        var lastIsHuman = last is not null && humans.Contains(last.FromAgent);

        var langLine = room.LanguageMode == LanguageMode.Human
            ? $"Reply in {room.Language ?? "the room's language"} (natural, human language)."
            : "Reply in terse machine-to-machine shorthand: abbreviations, minimal words, no pleasantries.";

        var humanLine = humans.Count > 0
            ? $"Human operator(s) in this room (people, NOT agents): {string.Join(", ", humans)}. "
              + "Treat them as the human user running you — when one speaks or @mentions you, answer them directly "
              + "and concretely, and do what they ask. Their word overrides agent-to-agent chatter."
            : "";

        var recent = msgs.Count > _opt.ContextLines ? msgs.Skip(msgs.Count - _opt.ContextLines) : msgs;
        var transcript = string.Join("\n", recent.Select(m =>
            $"{m.FromAgent}{(humans.Contains(m.FromAgent) ? " (human)" : "")}: {m.Body}"));
        if (transcript.Length == 0) transcript = "(no messages yet — you start)";

        var kick = msgs.Count == 0
            ? "Greet the other member(s) and kick off the conversation on the topic."
            : lastIsHuman
                ? $"The human '{last!.FromAgent}' just addressed the room. Answer them directly and helpfully — do the specific thing they asked."
                : "Respond to what was just said only if it moves things forward — a new point, a decision, or the next piece of the deliverable. "
                  + "If you have nothing new or actionable to add, post nothing. Never repeat a prior message.";

        var other = memberNames.FirstOrDefault(n => n != name);
        var mentionHint = other is null ? "another member by name" : $"another member by name (e.g. @{other})";

        var roleLine = string.IsNullOrWhiteSpace(agent.Role) ? "" : $"Your role in this room: {agent.Role}.";
        var writeLine = agent.Write
            ? "You MAY create or edit files to implement what the room has agreed. After changing anything, post a short summary of exactly what you changed (which files, and why)."
            : "Do NOT create or edit any files — take part by discussing and posting to the room only.";

        return $"""
                You are the agent "{name}" in a live Continuum room conversation with other AI agents and possibly a human. {roleLine} This is a working conversation with a goal — drive toward a concrete conclusion or deliverable, then stop. It is not open-ended chit-chat.

                Room: "{room.Name}"
                Topic: {room.Topic}
                {langLine}
                {humanLine}

                Recent conversation (oldest first; "(human)" marks a human, everyone else is an AI agent):
                {transcript}

                Your task: {kick} Keep any message short (1-4 sentences, or a few shorthand tokens). Speak as yourself ("{name}"); you may briefly mention what you are working on if relevant. You can @mention {mentionHint} to direct a question at them.

                Post AT MOST ONE message, and only if you have something genuinely new or actionable to add: call your Continuum channel_post tool with fromAgent="{name}", channel="{room.ChannelName}", body="<your message>". If you have nothing new to add, or the discussion has already reached its conclusion, do NOT call channel_post at all — just stop. When the room's objective is resolved (a decision is made or the deliverable is ready), post one final message whose body BEGINS with "[DONE]" and states the outcome; that ends the room. {writeLine}
                """;
    }

    // ---- spawning a turn ----

    private async Task RunTurnAsync(LocalAgent agent, string cli, string prompt, CancellationToken ct)
    {
        var logPath = Path.Combine(_opt.LogDir, $"{agent.Name}.log");
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = cli,
                WorkingDirectory = agent.Path,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            AddRuntimeArgs(psi, agent, prompt);

            using var proc = new Process { StartInfo = psi };
            var sb = new StringBuilder();
            proc.OutputDataReceived += (_, e) => { if (e.Data is not null) sb.AppendLine(e.Data); };
            proc.ErrorDataReceived += (_, e) => { if (e.Data is not null) sb.AppendLine(e.Data); };

            if (!proc.Start())
            {
                log.LogWarning("Room runner: failed to start claude for '{Agent}'", agent.Name);
                return;
            }
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
            await proc.WaitForExitAsync(ct);

            try { await File.AppendAllTextAsync(logPath, sb.ToString(), ct); } catch { /* best-effort log */ }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogWarning(ex, "Room runner: turn for '{Agent}' failed", agent.Name);
        }
    }

    /// <summary>Headless invocation per runtime. Write-capability is enforced by flags where the runtime allows.</summary>
    private void AddRuntimeArgs(ProcessStartInfo psi, LocalAgent agent, string prompt)
    {
        switch (agent.Runtime.Trim().ToLowerInvariant())
        {
            case "codex":
                // codex exec: non-interactive, no approval prompts. Sandbox controls edit capability (hard).
                psi.ArgumentList.Add("exec");
                psi.ArgumentList.Add("--sandbox");
                psi.ArgumentList.Add(agent.Write ? "workspace-write" : "read-only");
                psi.ArgumentList.Add(prompt);
                break;

            case "cursor":
                // cursor-agent print mode; --force is required for MCP tools to work headlessly.
                // No read-only sandbox flag exists, so write=false is only prompt-enforced.
                psi.ArgumentList.Add("--force");
                psi.ArgumentList.Add("-p");
                psi.ArgumentList.Add(prompt);
                break;

            default: // claude
                psi.ArgumentList.Add("-p");
                psi.ArgumentList.Add(prompt);
                psi.ArgumentList.Add("--allowedTools");
                psi.ArgumentList.Add(agent.Write ? $"{_opt.AllowedTools},Edit,Write,Bash" : _opt.AllowedTools);
                break;
        }
    }

    // ---- config / runtime resolution ----

    /// <summary>Resolve (and cache) the CLI path for a runtime; null if it isn't installed.</summary>
    private string? ResolveRuntime(string runtime)
    {
        var key = string.IsNullOrWhiteSpace(runtime) ? "claude" : runtime.Trim().ToLowerInvariant();
        if (_cli.TryGetValue(key, out var cached)) return cached;

        var isWin = OperatingSystem.IsWindows();
        string[] names = key switch
        {
            "codex"  => isWin ? ["codex.exe", "codex.cmd", "codex.bat", "codex"] : ["codex"],
            "cursor" => isWin ? ["cursor-agent.exe", "cursor-agent.cmd", "cursor-agent.bat", "cursor-agent"] : ["cursor-agent"],
            _        => isWin ? ["claude.exe", "claude.cmd", "claude.bat", "claude"] : ["claude"],
        };
        var configured = key == "claude" ? _opt.ClaudePath : null;
        var resolved = ResolveCli(configured, names);
        _cli[key] = resolved;
        return resolved;
    }

    private List<LocalAgent> LoadAgents()
    {
        // The historical agents.json wins so existing installs keep working; otherwise use appsettings.
        if (File.Exists(_opt.AgentsFile))
        {
            try
            {
                var json = File.ReadAllText(_opt.AgentsFile);
                var agents = JsonSerializer.Deserialize<List<LocalAgent>>(json,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web));
                if (agents is not null)
                    return [.. agents.Where(a => !string.IsNullOrWhiteSpace(a.Name) && !string.IsNullOrWhiteSpace(a.Path))];
            }
            catch (Exception ex)
            {
                log.LogWarning("Room runner: could not read {File}: {Msg}", _opt.AgentsFile, ex.Message);
            }
        }
        return [.. _opt.Agents.Where(a => !string.IsNullOrWhiteSpace(a.Name) && !string.IsNullOrWhiteSpace(a.Path))];
    }

    /// <summary>Find a CLI cross-platform: explicit config, then PATH, then ~/.local/bin.</summary>
    private static string? ResolveCli(string? configured, string[] names)
    {
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured)) return configured;

        var pathDirs = (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var extraDirs = new[] { Path.Combine(home, ".local", "bin") };

        foreach (var dir in pathDirs.Concat(extraDirs))
        {
            foreach (var n in names)
            {
                var candidate = Path.Combine(dir, n);
                if (File.Exists(candidate)) return candidate;
            }
        }
        return null;
    }
}
