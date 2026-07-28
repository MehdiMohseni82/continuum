using Continuum.Core.Domain;

namespace Continuum.Core.Contracts;

public sealed record MemorySaveRequest
{
    public MemoryType Type { get; init; } = MemoryType.Project;
    public required string Content { get; init; }
    public Guid? WorkspaceId { get; init; }
    public Guid? SourceSessionId { get; init; }
    public bool Pinned { get; init; }
}

public sealed record MemoryDto(
    Guid Id,
    MemoryType Type,
    string Content,
    float Salience,
    bool Pinned,
    Guid? WorkspaceId,
    DateTimeOffset CreatedAt,
    double? Score);

public sealed record CheckpointRequest(Guid SessionId, string Content, string Reason);

public sealed record CheckpointDto(
    Guid Id, Guid SessionId, string Content, string Reason, DateTimeOffset CreatedAt);

/// <summary>What a SessionStart / UserPromptSubmit hook injects into the model's context.</summary>
public sealed record ContextInjection(string AdditionalContext);
