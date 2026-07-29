using Continuum.Core.Domain;

namespace Continuum.Core.Contracts;

public sealed record CreateRoomRequest(string Name, string Topic, LanguageMode LanguageMode, string? Language);
public sealed record AddMemberRequest(string Agent);
public sealed record RoomPostRequest(string FromAgent, string Body);

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
    DateTimeOffset? LastActivityAt);

public sealed record RoomDetailDto(
    RoomDto Room,
    IReadOnlyList<RoomMemberDto> Members,
    IReadOnlyList<MessageDto> Messages);
