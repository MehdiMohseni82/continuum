using System.ComponentModel;
using System.Text;
using Continuum.Core.Domain;
using ModelContextProtocol.Server;

namespace Continuum.Mcp;

/// <summary>
/// The tools Claude Code calls. Each is a thin wrapper over the backend API; return values are
/// human-readable text the model can act on directly.
/// </summary>
[McpServerToolType]
public static class ContinuumTools
{
    [McpServerTool(Name = "memory_save"), Description(
        "Save a durable fact so it survives across sessions and machines. type is one of " +
        "User, Feedback, Project, Reference. Pass projectKey to scope it to a project, or omit for global. " +
        "Secrets are redacted automatically before storage.")]
    public static async Task<string> MemorySave(
        BackendApi api,
        [Description("The fact to remember, in plain language.")] string content,
        [Description("User | Feedback | Project | Reference")] string type = "Project",
        [Description("Optional project directory key to scope the memory.")] string? projectKey = null,
        [Description("Optional source session id for provenance.")] string? sessionId = null,
        [Description("Pin so it never decays or gets pruned.")] bool pinned = false,
        CancellationToken ct = default)
    {
        var t = Enum.TryParse<MemoryType>(type, ignoreCase: true, out var parsed) ? parsed : MemoryType.Project;
        Guid? sid = Guid.TryParse(sessionId, out var s) ? s : null;
        var saved = await api.SaveMemoryAsync(content, t, projectKey, sid, pinned, ct);
        return $"Saved [{saved.Type}] memory {saved.Id}.";
    }

    [McpServerTool(Name = "memory_search"), Description(
        "Recall the most relevant durable memories for a query, using semantic similarity. " +
        "Pass projectKey to bias toward a project (global memories are always included).")]
    public static async Task<string> MemorySearch(
        BackendApi api,
        [Description("What you want to recall.")] string query,
        [Description("Optional project directory key.")] string? projectKey = null,
        [Description("Max results (1-50).")] int limit = 8,
        CancellationToken ct = default)
    {
        var hits = await api.SearchMemoryAsync(query, projectKey, Math.Clamp(limit, 1, 50), ct);
        if (hits.Count == 0) return "No relevant memories.";
        var sb = new StringBuilder();
        foreach (var m in hits)
            // Invariant culture: the machine's locale was rendering the score as "0,75". Tool output
            // is read by a model, not a person in a locale, so a comma decimal is just wrong here.
            sb.Append("- [").Append(m.Type)
              .Append(m.Score is { } sc ? $" {sc.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}" : "")
              .Append("] ").AppendLine(m.Content);
        return sb.ToString();
    }

    [McpServerTool(Name = "memory_list"), Description("List stored memories, most important first. Optionally scope by projectKey.")]
    public static async Task<string> MemoryList(
        BackendApi api,
        [Description("Optional project directory key.")] string? projectKey = null,
        [Description("Max results.")] int limit = 30,
        CancellationToken ct = default)
    {
        var items = await api.ListMemoryAsync(projectKey, Math.Clamp(limit, 1, 200), ct);
        if (items.Count == 0) return "No memories stored.";
        var sb = new StringBuilder();
        foreach (var m in items)
            sb.Append(m.Pinned ? "📌 " : "- ").Append('[').Append(m.Id).Append("] [").Append(m.Type).Append("] ").AppendLine(m.Content);
        return sb.ToString();
    }

    [McpServerTool(Name = "memory_forget"), Description("Delete a memory by its id (from memory_list).")]
    public static async Task<string> MemoryForget(
        BackendApi api,
        [Description("The memory id to delete.")] string id,
        CancellationToken ct = default)
    {
        if (!Guid.TryParse(id, out var gid)) return "Invalid id.";
        return await api.ForgetMemoryAsync(gid, ct) ? $"Forgot {gid}." : "No such memory.";
    }

    [McpServerTool(Name = "context_checkpoint"), Description(
        "Save a snapshot of the current working context (open threads, decisions, next steps) for a session, " +
        "so it can be restored later or on another machine.")]
    public static async Task<string> ContextCheckpoint(
        BackendApi api,
        [Description("The session id this checkpoint belongs to.")] string sessionId,
        [Description("Markdown snapshot of open threads, decisions, and next steps.")] string content,
        [Description("Why: manual | pre-compact | stop")] string reason = "manual",
        CancellationToken ct = default)
    {
        if (!Guid.TryParse(sessionId, out var sid)) return "Invalid sessionId.";
        var cp = await api.CheckpointAsync(sid, content, reason, ct);
        return $"Checkpoint {cp.Id} saved for session {sid}.";
    }

    [McpServerTool(Name = "workspace_list"), Description(
        "List all projects (workspaces) Continuum tracks, with their projectKey, friendly name, and session count. " +
        "Use the projectKey with workspace_rename or to scope memory tools.")]
    public static async Task<string> WorkspaceList(BackendApi api, CancellationToken ct = default)
    {
        var items = await api.ListWorkspacesAsync(ct);
        if (items.Count == 0) return "No projects yet.";
        var sb = new StringBuilder();
        foreach (var w in items.OrderByDescending(w => w.SessionCount))
            sb.Append("- ").Append(w.DisplayName).Append("  (").Append(w.SessionCount).Append(" sessions)  key=")
              .AppendLine(w.ProjectKey);
        return sb.ToString();
    }

    [McpServerTool(Name = "workspace_rename"), Description(
        "Give a project a friendly display name, shown everywhere its sessions and memories appear. " +
        "Identify the project by its projectKey (from workspace_list). Applies retroactively to all its history.")]
    public static async Task<string> WorkspaceRename(
        BackendApi api,
        [Description("The project directory key to rename, e.g. D--dotnet-talk-projects-agent-talk.")] string projectKey,
        [Description("The new friendly name.")] string name,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(projectKey) || string.IsNullOrWhiteSpace(name))
            return "Both projectKey and name are required.";
        var renamed = await api.RenameWorkspaceAsync(projectKey.Trim(), name.Trim(), ct);
        return renamed is null ? $"No project with key '{projectKey}'." : $"Renamed '{projectKey}' → \"{renamed}\".";
    }

    [McpServerTool(Name = "history_search"), Description(
        "Full-text search across every past session on every machine. Use to find when/where something was done.")]
    public static async Task<string> HistorySearch(
        BackendApi api,
        [Description("Search phrase.")] string query,
        [Description("Max results.")] int limit = 10,
        CancellationToken ct = default)
    {
        var hits = await api.SearchHistoryAsync(query, Math.Clamp(limit, 1, 50), ct);
        if (hits.Count == 0) return "No matches.";
        var sb = new StringBuilder();
        foreach (var h in hits)
            sb.Append("- ").Append(h.Workspace).Append(" · ").Append(h.SessionTitle ?? "(untitled)")
              .Append(" · ").Append(h.Timestamp.ToString("yyyy-MM-dd HH:mm")).Append("\n  ").AppendLine(h.Snippet ?? "");
        return sb.ToString();
    }
}
