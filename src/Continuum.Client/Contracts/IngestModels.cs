using System.Text.Json;

namespace Continuum.Core.Contracts;

/// <summary>A batch of events pushed by the daemon. Shared by Host and Daemon.</summary>
public sealed record IngestBatch
{
    /// <summary>Stable machine name this batch originated from.</summary>
    public required string MachineName { get; init; }

    public required IReadOnlyList<IngestEvent> Events { get; init; }
}

/// <summary>
/// One parsed transcript line, ready to ingest. Carries just enough session context
/// (project key, timestamps, title) for the server to upsert the parent session.
/// </summary>
public sealed record IngestEvent
{
    public Guid SessionId { get; init; }

    /// <summary>Project directory name (the workspace key).</summary>
    public required string ProjectKey { get; init; }

    public Guid Uuid { get; init; }
    public Guid? ParentUuid { get; init; }

    public required string Type { get; init; }
    public string? Role { get; init; }

    public DateTimeOffset Timestamp { get; init; }

    public string? Text { get; init; }

    public string? CcVersion { get; init; }
    public string? GitBranch { get; init; }

    /// <summary>Set when the line names the session (e.g. an ai-title line), used to update the session title.</summary>
    public string? Title { get; init; }

    /// <summary>The full original JSON line.</summary>
    public JsonElement Raw { get; init; }
}

/// <summary>Server response to an ingest batch.</summary>
public sealed record IngestResult
{
    public int Received { get; init; }
    public int Inserted { get; init; }
    public int Duplicates { get; init; }
}
