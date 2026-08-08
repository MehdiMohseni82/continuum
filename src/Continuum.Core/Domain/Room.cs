namespace Continuum.Core.Domain;

/// <summary>
/// A named space where multiple bus agents hold an open-ended conversation on a topic. Messages are
/// ordinary channel messages (see <see cref="ChannelName"/>), so agents talk with the existing
/// channel_post/channel_read tools. An admin creates rooms and closes them; closing ends the chat.
/// </summary>
public class Room
{
    public Guid Id { get; set; }

    /// <summary>Short display title, unique per owner.</summary>
    public required string Name { get; set; }

    /// <summary>What the agents should talk about (frames the conversation).</summary>
    public required string Topic { get; set; }

    /// <summary>Standing system-prompt framing fed to each agent when it joins the room: its role, the
    /// goal, and the rules of engagement. Authored at room creation and delivered once on join (not
    /// repeated per message), so the per-turn messages stay raw — exactly like a normal CLI chat.</summary>
    public string? SystemPrompt { get; set; }

    public LanguageMode LanguageMode { get; set; } = LanguageMode.Human;

    /// <summary>The human language to speak when <see cref="LanguageMode"/> is Human (e.g. "English", "Farsi"). Null for Shorthand.</summary>
    public string? Language { get; set; }

    /// <summary>"open" | "closed" — mirrors Handoff.Status. Closing the room stops the conversation.</summary>
    public string Status { get; set; } = "open";

    /// <summary>The backing bus channel that carries this room's messages.</summary>
    public required string ChannelName { get; set; }

    /// <summary>The admin who owns the room.</summary>
    public Guid OwnerId { get; set; } = Defaults.DefaultOwnerId;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }

    public List<RoomMember> Members { get; } = [];
}

/// <summary>An agent that has joined a <see cref="Room"/>.</summary>
public class RoomMember
{
    public Guid Id { get; set; }

    public Guid RoomId { get; set; }
    public Room? Room { get; set; }

    public Guid AgentId { get; set; }
    public Agent? Agent { get; set; }

    public DateTimeOffset JoinedAt { get; set; }
}
