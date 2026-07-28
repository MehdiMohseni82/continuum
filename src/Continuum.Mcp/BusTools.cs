using System.ComponentModel;
using System.Text;
using ModelContextProtocol.Server;

namespace Continuum.Mcp;

/// <summary>Inter-agent bus tools: registration, direct messages, channels, and task hand-offs.</summary>
[McpServerToolType]
public static class BusTools
{
    [McpServerTool(Name = "agent_register"), Description(
        "Register this session as a named agent on the bus so other agents can message it or hand it work.")]
    public static async Task<string> AgentRegister(
        BackendApi api,
        [Description("Handle for this agent, e.g. 'researcher' or 'implementer'.")] string name,
        [Description("Optional machine name.")] string? machine = null,
        [Description("Optional free-text list of what this agent can do.")] string? capabilities = null,
        CancellationToken ct = default)
    {
        var a = await api.RegisterAgentAsync(name, machine, capabilities, ct);
        return $"Registered agent '{a.Name}' ({a.Id}).";
    }

    [McpServerTool(Name = "agent_list"), Description("List agents currently known to the bus, most recently seen first.")]
    public static async Task<string> AgentList(BackendApi api, CancellationToken ct = default)
    {
        var agents = await api.ListAgentsAsync(ct);
        if (agents.Count == 0) return "No agents registered.";
        var sb = new StringBuilder();
        foreach (var a in agents)
            sb.Append("- ").Append(a.Name)
              .Append(a.Capabilities is null ? "" : $" — {a.Capabilities}")
              .Append(" (last seen ").Append(a.LastSeenAt.ToString("HH:mm:ss")).AppendLine(")");
        return sb.ToString();
    }

    [McpServerTool(Name = "bus_send"), Description("Send a direct message from one agent to another.")]
    public static async Task<string> BusSend(
        BackendApi api,
        [Description("Sender agent name.")] string fromAgent,
        [Description("Recipient agent name.")] string toAgent,
        [Description("Message body.")] string body,
        CancellationToken ct = default)
    {
        var m = await api.SendDirectAsync(fromAgent, toAgent, body, ct);
        return $"Sent to {toAgent} (msg {m.Id}).";
    }

    [McpServerTool(Name = "bus_inbox"), Description(
        "Read (and mark read) messages addressed directly to an agent. Returns unread by default.")]
    public static async Task<string> BusInbox(
        BackendApi api,
        [Description("Agent name whose inbox to read.")] string agent,
        [Description("Only unread messages.")] bool unreadOnly = true,
        CancellationToken ct = default)
    {
        var msgs = await api.InboxAsync(agent, unreadOnly, ct);
        if (msgs.Count == 0) return "Inbox empty.";
        var sb = new StringBuilder();
        foreach (var m in msgs)
            sb.Append("- from ").Append(m.FromAgent).Append(" @ ").Append(m.CreatedAt.ToString("HH:mm:ss")).Append(": ").AppendLine(m.Body);
        return sb.ToString();
    }

    [McpServerTool(Name = "channel_post"), Description("Post a message to a named channel (created on first use).")]
    public static async Task<string> ChannelPost(
        BackendApi api,
        [Description("Sender agent name.")] string fromAgent,
        [Description("Channel name.")] string channel,
        [Description("Message body.")] string body,
        CancellationToken ct = default)
    {
        var m = await api.PostChannelAsync(fromAgent, channel, body, ct);
        return $"Posted to #{channel} (msg {m.Id}).";
    }

    [McpServerTool(Name = "channel_read"), Description("Read recent messages from a channel. Use 'since' (a message id) to page.")]
    public static async Task<string> ChannelRead(
        BackendApi api,
        [Description("Channel name.")] string channel,
        [Description("Only messages with id greater than this.")] long since = 0,
        [Description("Max messages.")] int take = 50,
        CancellationToken ct = default)
    {
        var msgs = await api.ReadChannelAsync(channel, since, Math.Clamp(take, 1, 500), ct);
        if (msgs.Count == 0) return "No messages.";
        var sb = new StringBuilder();
        foreach (var m in msgs)
            sb.Append('[').Append(m.Id).Append("] ").Append(m.FromAgent).Append(": ").AppendLine(m.Body);
        return sb.ToString();
    }

    [McpServerTool(Name = "handoff_create"), Description(
        "Package a task for another agent to pick up, with an optional context pointer (session id, checkpoint id, or note).")]
    public static async Task<string> HandoffCreate(
        BackendApi api,
        [Description("The agent creating the hand-off.")] string fromAgent,
        [Description("Short title.")] string title,
        [Description("The task to be done.")] string task,
        [Description("Optional context pointer.")] string? contextRef = null,
        CancellationToken ct = default)
    {
        var h = await api.CreateHandoffAsync(fromAgent, title, task, contextRef, ct);
        return $"Created hand-off {h.Id}: {h.Title}.";
    }

    [McpServerTool(Name = "handoff_claim"), Description("Claim an open hand-off by its id, marking it as yours.")]
    public static async Task<string> HandoffClaim(
        BackendApi api,
        [Description("The hand-off id.")] string id,
        [Description("The agent claiming it.")] string byAgent,
        CancellationToken ct = default)
    {
        if (!Guid.TryParse(id, out var gid)) return "Invalid id.";
        var h = await api.ClaimHandoffAsync(gid, byAgent, ct);
        return h is null ? "Hand-off is not open (already claimed?)." : $"Claimed '{h.Title}'.";
    }

    [McpServerTool(Name = "handoff_list"), Description("List hand-offs, optionally filtered by status: open | claimed | done.")]
    public static async Task<string> HandoffList(
        BackendApi api,
        [Description("Optional status filter.")] string? status = null,
        CancellationToken ct = default)
    {
        var hs = await api.ListHandoffsAsync(status, ct);
        if (hs.Count == 0) return "No hand-offs.";
        var sb = new StringBuilder();
        foreach (var h in hs)
            sb.Append("- [").Append(h.Status).Append("] ").Append(h.Id).Append(" — ").Append(h.Title)
              .Append(" (from ").Append(h.FromAgent).Append(h.ClaimedBy is null ? "" : $", claimed by {h.ClaimedBy}").AppendLine(")");
        return sb.ToString();
    }
}
