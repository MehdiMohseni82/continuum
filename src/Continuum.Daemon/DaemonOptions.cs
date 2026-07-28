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
}
