namespace Continuum.Core.Contracts;

/// <summary>A gzip database dump written by the backup sidecar.</summary>
public sealed record BackupFileDto(string Name, long SizeBytes, DateTimeOffset CreatedAt);

public sealed record BackupStatusDto(
    bool Configured,
    string Directory,
    int Count,
    long TotalBytes,
    DateTimeOffset? LatestAt,
    IReadOnlyList<BackupFileDto> Recent);

/// <summary>The daily rollup posted to the "digest" bus channel.</summary>
public sealed record DigestDto(
    DateTimeOffset GeneratedAt,
    int SessionsActive,
    int Events,
    int MemoriesAdded,
    IReadOnlyList<CountByLabel> TopWorkspaces,
    string Markdown);
