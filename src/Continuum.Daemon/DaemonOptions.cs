namespace Continuum.Daemon;

public sealed class DaemonOptions
{
    /// <summary>Root Claude Code directory to watch. Inside a container this is the read-only mount.</summary>
    public string ClaudeDir { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");

    public string BackendUrl { get; set; } = "http://localhost:5000";

    public string Token { get; set; } = "dev-local-token-change-me";

    public string MachineName { get; set; } = Environment.MachineName;

    /// <summary>Where the daemon persists its per-file read cursors (SQLite).</summary>
    public string CursorDbPath { get; set; } = "continuum-cursors.db";

    public int PollSeconds { get; set; } = 3;

    /// <summary>Max events per upload; large files are sent in several batches.</summary>
    public int BatchSize { get; set; } = 500;

    /// <summary>Drives local agents through their turns in open rooms (cross-platform successor to room-runner.ps1).</summary>
    public RoomRunnerOptions RoomRunner { get; set; } = new();
}

/// <summary>
/// Configuration for the in-daemon room runner. Local agents are read from <see cref="AgentsFile"/>
/// when it exists (the historical ~/Continuum/rooms/agents.json), otherwise from <see cref="Agents"/>.
/// </summary>
public sealed class RoomRunnerOptions
{
    private static string RoomsDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Continuum", "rooms");

    /// <summary>Turn room-driving on/off without stopping history backfill.</summary>
    public bool Enabled { get; set; } = true;

    public int IntervalSeconds { get; set; } = 35;

    /// <summary>Transcript lines fed to each turn.</summary>
    public int ContextLines { get; set; } = 12;

    /// <summary>Path to the claude CLI. Empty ⇒ auto-detect from PATH / ~/.local/bin.</summary>
    public string? ClaudePath { get; set; }

    public string AllowedTools { get; set; } = "mcp__continuum,Read,Grep,Glob";

    /// <summary>JSON list of { name, path } local agents. Wins over <see cref="Agents"/> when present.</summary>
    public string AgentsFile { get; set; } = Path.Combine(RoomsDir, "agents.json");

    /// <summary>Inline fallback when <see cref="AgentsFile"/> is absent.</summary>
    public List<LocalAgent> Agents { get; set; } = [];

    /// <summary>Where per-agent turn output is written (one log per agent).</summary>
    public string LogDir { get; set; } = RoomsDir;
}

public sealed class LocalAgent
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";

    /// <summary>Which CLI drives this agent's turns: "claude" (default), "codex", or "cursor".</summary>
    public string Runtime { get; set; } = "claude";

    /// <summary>
    /// Whether this agent may create/edit files. Default false = it only discusses and posts to the room.
    /// Hard-enforced for claude (read-only tool set) and codex (--sandbox read-only); prompt-enforced for
    /// cursor (its print mode has no read-only sandbox flag).
    /// </summary>
    public bool Write { get; set; }

    /// <summary>Optional role label injected into the turn prompt, e.g. "consultant" or "implementer".</summary>
    public string? Role { get; set; }
}
