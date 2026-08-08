using Continuum.Core.Domain;

namespace Continuum.Core.Contracts;

public sealed record CreateRoomRequest(string Name, string Topic, LanguageMode LanguageMode, string? Language, string? SystemPrompt = null);
public sealed record AddMemberRequest(string Agent);
public sealed record RoomPostRequest(string FromAgent, string Body,
    int? InputTokens = null, int? OutputTokens = null, int? CacheReadTokens = null, int? CacheCreationTokens = null);

/// <summary>Ask a server-side (Claude API) agent to take a turn now. Optional steer directs the message;
/// optional agent picks which server agent speaks (defaults to the first configured one in the room).</summary>
public sealed record LeadRequest(string? Steer, string? Agent);

public sealed record RoomMemberDto(string Agent, string? MachineName, DateTimeOffset JoinedAt);

public sealed record RoomDto(
    Guid Id,
    string Name,
    string Topic,
    LanguageMode LanguageMode,
    string? Language,
    string Status,
    string ChannelName,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ClosedAt,
    int MemberCount,
    int MessageCount,
    DateTimeOffset? LastActivityAt,
    string? SystemPrompt = null,
    long TotalTokens = 0);

public sealed record RoomDetailDto(
    RoomDto Room,
    IReadOnlyList<RoomMemberDto> Members,
    IReadOnlyList<MessageDto> Messages);
