using Continuum.Core.Domain;

namespace Continuum.Core.Contracts;

public sealed record WorkspaceDto(Guid Id, string ProjectKey, string DisplayName, int SessionCount);

public sealed record RenameWorkspaceRequest(string DisplayName);

/// <summary>Adopt a new project key for an existing workspace, carrying all of its history with it.</summary>
public sealed record RekeyWorkspaceRequest(string ProjectKey);

/// <summary>Outcome of a re-key: the key is unique, so it can already belong to someone else.</summary>
public enum RekeyResult
{
    Ok,
    NotFound,
    /// <summary>Another workspace already answers to that key; merging the two is a separate act.</summary>
    Conflict,
    Invalid,
}

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
