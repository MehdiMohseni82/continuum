namespace Continuum.Core.Domain;

/// <summary>A message on the bus — either direct (ToAgentId set) or broadcast to a channel (ChannelId set).</summary>
public class AgentMessage
{
    public long Id { get; set; }

    public Guid FromAgentId { get; set; }
    public Agent? FromAgent { get; set; }

    /// <summary>Set for a direct message.</summary>
    public Guid? ToAgentId { get; set; }

    /// <summary>Set for a channel post.</summary>
    public Guid? ChannelId { get; set; }

    public required string Body { get; set; }

    /// <summary>Whether the recipient has pulled it from their inbox (direct messages only).</summary>
    public bool Read { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
