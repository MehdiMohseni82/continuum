using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Continuum.Core.Sessions;

/// <summary>
/// One Claude Code session as it advertises itself on disk. Claude Code writes a small JSON file per
/// session under <see cref="LocalSessionRegistry.DefaultDirectory"/> and keeps it current, so reading
/// that directory tells us every session on this machine — its name, working directory and whether it
/// is idle or busy — without talking to any of them.
/// </summary>
/// <param name="Pid">Process id of the session, and the registry file's own name.</param>
/// <param name="SessionId">Claude Code's session id; the same id Continuum ingests transcripts under.</param>
/// <param name="Cwd">Working directory the session was started in.</param>
/// <param name="Name">The name the session answers to (derived from its folder unless renamed).</param>
/// <param name="Status">Claude Code's own status word, e.g. <c>idle</c> or <c>busy</c>.</param>
/// <param name="Kind">Session kind, e.g. <c>interactive</c>.</param>
/// <param name="Entrypoint">How it was launched, e.g. <c>cli</c>.</param>
/// <param name="Version">Claude Code version string.</param>
/// <param name="MessagingSocketPath">Its cross-session messaging inbox socket, when it binds one.</param>
public sealed record LocalSession(
    int Pid,
    string SessionId,
    string Cwd,
    string Name,
    string Status,
    string Kind,
    string Entrypoint,
    string Version,
    string MessagingSocketPath,
    DateTimeOffset? StartedAt,
    DateTimeOffset? UpdatedAt)
{
    /// <summary>The session id as a <see cref="Guid"/>, or null when Claude Code writes a form we can't parse.</summary>
    public Guid? SessionGuid => Guid.TryParse(SessionId, out var g) ? g : null;

    /// <summary>True when the session reports itself as actively working on a turn.</summary>
    public bool IsBusy => string.Equals(Status, "busy", StringComparison.OrdinalIgnoreCase);

    /// <summary>True when the session advertises an inbox socket that still exists on disk.</summary>
    public bool HasInbox => MessagingSocketPath.Length > 0 && File.Exists(MessagingSocketPath);
}

/// <summary>
/// Reads Claude Code's on-disk session registry. This is a read-only observer: it never writes to the
/// directory and never speaks to a session's messaging socket, which is owner-bound and carries an
/// undocumented protocol. Parsing is deliberately tolerant, matching the rest of Continuum's stance on
/// format drift — an unreadable or unfamiliar entry is skipped, never thrown, so a newer Claude Code
/// can change the file without stopping the daemon.
/// </summary>
public sealed class LocalSessionRegistry
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private readonly string _directory;
    private readonly Func<int, bool> _isProcessAlive;

    /// <summary>Where Claude Code keeps the registry: <c>~/.claude/sessions</c>.</summary>
    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "sessions");

    /// <param name="directory">Registry directory; defaults to <see cref="DefaultDirectory"/>.</param>
    /// <param name="isProcessAlive">Liveness test, injectable so tests don't depend on real processes.</param>
    public LocalSessionRegistry(string? directory = null, Func<int, bool>? isProcessAlive = null)
    {
        _directory = directory ?? DefaultDirectory;
        _isProcessAlive = isProcessAlive ?? IsProcessAlive;
    }

    /// <summary>
    /// Every entry in the registry, newest first. Includes sessions whose process has exited — the file
    /// outlives the process — so prefer <see cref="ReadLive"/> unless you specifically want the history.
    /// </summary>
    public IReadOnlyList<LocalSession> Read()
    {
        if (!Directory.Exists(_directory)) return [];

        var found = new List<LocalSession>();
        foreach (var path in Directory.EnumerateFiles(_directory, "*.json"))
        {
            var session = TryParse(path);
            if (session is not null) found.Add(session);
        }

        return [.. found.OrderByDescending(s => s.StartedAt ?? DateTimeOffset.MinValue)];
    }

    /// <summary>Registry entries whose process is still running.</summary>
    public IReadOnlyList<LocalSession> ReadLive() => [.. Read().Where(s => _isProcessAlive(s.Pid))];

    private static LocalSession? TryParse(string path)
    {
        Entry? e;
        try { e = JsonSerializer.Deserialize<Entry>(File.ReadAllText(path), Json); }
        catch (JsonException) { return null; }   // malformed or a shape we don't recognise
        catch (IOException) { return null; }     // being rewritten as we read, or vanished
        catch (UnauthorizedAccessException) { return null; }

        // A session we can neither identify nor address is of no use to us.
        if (e is null || e.Pid <= 0 || string.IsNullOrWhiteSpace(e.SessionId)) return null;

        return new LocalSession(
            Pid: e.Pid,
            SessionId: e.SessionId,
            Cwd: e.Cwd ?? "",
            Name: e.Name ?? "",
            Status: e.Status ?? "",
            Kind: e.Kind ?? "",
            Entrypoint: e.Entrypoint ?? "",
            Version: e.Version ?? "",
            MessagingSocketPath: e.MessagingSocketPath ?? "",
            StartedAt: FromUnixMs(e.StartedAt),
            UpdatedAt: FromUnixMs(e.UpdatedAt));
    }

    private static DateTimeOffset? FromUnixMs(long? ms)
    {
        if (ms is null or <= 0) return null;
        try { return DateTimeOffset.FromUnixTimeMilliseconds(ms.Value); }
        catch (ArgumentOutOfRangeException) { return null; }
    }

    private static bool IsProcessAlive(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            return !p.HasExited;
        }
        catch (ArgumentException) { return false; }      // no such process
        catch (InvalidOperationException) { return false; }
    }

    /// <summary>Wire shape of a registry file. Unknown members are ignored by design.</summary>
    private sealed class Entry
    {
        [JsonPropertyName("pid")] public int Pid { get; set; }
        [JsonPropertyName("sessionId")] public string? SessionId { get; set; }
        [JsonPropertyName("cwd")] public string? Cwd { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("status")] public string? Status { get; set; }
        [JsonPropertyName("kind")] public string? Kind { get; set; }
        [JsonPropertyName("entrypoint")] public string? Entrypoint { get; set; }
        [JsonPropertyName("version")] public string? Version { get; set; }
        [JsonPropertyName("messagingSocketPath")] public string? MessagingSocketPath { get; set; }
        [JsonPropertyName("startedAt")] public long? StartedAt { get; set; }
        [JsonPropertyName("updatedAt")] public long? UpdatedAt { get; set; }
    }
}
