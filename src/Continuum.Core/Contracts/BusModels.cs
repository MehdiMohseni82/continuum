namespace Continuum.Core.Contracts;

public sealed record RegisterAgentRequest(string Name, string? MachineName, Guid? CurrentSessionId, string? Capabilities);

public sealed record AgentDto(
    Guid Id, string Name, string? MachineName, string? Capabilities, DateTimeOffset LastSeenAt);

public sealed record SendMessageRequest(string FromAgent, string ToAgent, string Body);
public sealed record ChannelPostRequest(string FromAgent, string Channel, string Body);

public sealed record MessageDto(
    long Id, string FromAgent, string? ToAgent, string? Channel, string Body, DateTimeOffset CreatedAt,
    int? InputTokens = null, int? OutputTokens = null, int? CacheReadTokens = null, int? CacheCreationTokens = null);

public sealed record HandoffRequest(string FromAgent, string Title, string Task, string? ContextRef, Guid? WorkspaceId);
public sealed record ClaimHandoffRequest(string ByAgent);

public sealed record HandoffDto(
    Guid Id, string FromAgent, string? ClaimedBy, string Title, string Task, string? ContextRef,
    string Status, DateTimeOffset CreatedAt, DateTimeOffset? ClaimedAt);
