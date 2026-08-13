namespace Continuum.Core.Domain;

/// <summary>A message on the bus — either direct (ToAgentId set) or broadcast to a channel (ChannelId set).</summary>
public class AgentMessage
{
    public long Id { get; set; }

    public Guid FromAgentId { get; set; }
    public Agent? FromAgent { get; set; }

    /// <summary>
    /// The person whose agent posted this. An agent belongs to someone, and once a room holds several
    /// people's agents, "who said this" is a question about people, not only about agent names — it is
    /// also what lets token spend be attributed per person. Null for messages posted before this
    /// existed, or by a server-side agent that belongs to no one.
    /// </summary>
    public Guid? FromUserId { get; set; }

    /// <summary>Set for a direct message.</summary>
    public Guid? ToAgentId { get; set; }

    /// <summary>Set for a channel post.</summary>
    public Guid? ChannelId { get; set; }

    public required string Body { get; set; }

    /// <summary>Whether the recipient has pulled it from their inbox (direct messages only).</summary>
    public bool Read { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    // Token usage of the agent turn that produced this message. Populated when a message is posted with
    // usage data (e.g. the room relay reads it from the session transcript); null for messages posted
    // without it. Used to show per-message and per-room token totals.
    public int? InputTokens { get; set; }
    public int? OutputTokens { get; set; }
    public int? CacheReadTokens { get; set; }
    public int? CacheCreationTokens { get; set; }
}
