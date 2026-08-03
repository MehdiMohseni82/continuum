namespace Continuum.Host.Services;

/// <summary>
/// Configuration for the optional server-side room agent(s) driven by the Claude API. Off by default.
/// One shared API key (config or the ANTHROPIC_API_KEY env var) backs every configured agent. Each
/// agent participates in a room by being added as a member, exactly like a local-CLI agent.
/// </summary>
public sealed class ServerAgentOptions
{
    /// <summary>Master switch for the autonomous loop. When false, the "Lead" button still works if a key is set.</summary>
    public bool Enabled { get; set; }

    /// <summary>Claude API key. Falls back to the ANTHROPIC_API_KEY environment variable when blank.</summary>
    public string? ApiKey { get; set; }

    public string Model { get; set; } = "claude-opus-5";

    /// <summary>Autonomous-loop poll interval.</summary>
    public int IntervalSeconds { get; set; } = 20;

    /// <summary>Output cap per turn — room messages are short.</summary>
    public int MaxTokens { get; set; } = 1024;

    /// <summary>Transcript lines fed to each turn.</summary>
    public int ContextLines { get; set; } = 12;

    /// <summary>
    /// Hard cap on consecutive agent turns with no human speaking. Once a room reaches it the room is
    /// closed instead of driven further — the backstop against two agents talking forever. A human
    /// message resets the count. 0 disables the cap.
    /// </summary>
    public int MaxAutonomousTurns { get; set; } = 16;

    /// <summary>The server-side agents this backend drives. Keep names distinct from any daemon agents.</summary>
    public List<ServerAgentDef> Agents { get; set; } = [];

    /// <summary>The effective key: explicit config wins, else the standard Anthropic env var.</summary>
    public string? ResolveApiKey() =>
        !string.IsNullOrWhiteSpace(ApiKey) ? ApiKey : Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");

    public bool HasKey() => !string.IsNullOrWhiteSpace(ResolveApiKey());
}

public sealed class ServerAgentDef
{
    public string Name { get; set; } = "";

    /// <summary>Optional role label injected into the turn prompt, e.g. "facilitator" or "consultant".</summary>
    public string? Role { get; set; }
}
