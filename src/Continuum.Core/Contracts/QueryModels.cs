using Continuum.Core.Domain;

namespace Continuum.Core.Contracts;

public sealed record WorkspaceDto(Guid Id, string ProjectKey, string DisplayName, int SessionCount);

public sealed record SessionSummaryDto(
    Guid Id,
    string? Title,
    string Workspace,
    string Machine,
    SessionStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset LastEventAt,
    int MessageCount);

public sealed record EventDto(
    long Id,
    Guid Uuid,
    string Type,
    string? Role,
    DateTimeOffset Timestamp,
    string? Text);

public sealed record SessionDetailDto(
    SessionSummaryDto Session,
    IReadOnlyList<EventDto> Events);

public sealed record SearchHitDto(
    Guid SessionId,
    string? SessionTitle,
    string Workspace,
    long EventId,
    string Type,
    DateTimeOffset Timestamp,
    string? Snippet);

public sealed record SessionSearchHit(
    Guid Id,
    string? Title,
    string Workspace,
    string Machine,
    string? Summary,
    DateTimeOffset LastEventAt,
    int MessageCount,
    double? Score);
