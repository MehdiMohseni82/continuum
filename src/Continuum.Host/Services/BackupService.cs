using Continuum.Core.Contracts;

namespace Continuum.Host.Services;

public sealed class BackupOptions
{
    /// <summary>Directory where the backup sidecar writes dumps (mounted read-only into the host). Empty = not configured.</summary>
    public string Directory { get; set; } = "";
}

/// <summary>
/// Read-only view over the gzip dumps produced by the backup sidecar. The host doesn't create
/// backups itself — it just reports what the sidecar has written to the shared volume.
/// </summary>
public sealed class BackupService(BackupOptions options)
{
    public BackupStatusDto Status(int recent = 10)
    {
        var dir = options.Directory;
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
            return new BackupStatusDto(false, dir, 0, 0, null, []);

        var files = new DirectoryInfo(dir)
            .EnumerateFiles("continuum-*.sql.gz")
            .Select(f => new BackupFileDto(f.Name, f.Length, new DateTimeOffset(f.LastWriteTimeUtc, TimeSpan.Zero)))
            .OrderByDescending(f => f.CreatedAt)
            .ToList();

        return new BackupStatusDto(
            Configured: true,
            Directory: dir,
            Count: files.Count,
            TotalBytes: files.Sum(f => f.SizeBytes),
            LatestAt: files.Count > 0 ? files[0].CreatedAt : null,
            Recent: files.Take(Math.Clamp(recent, 1, 50)).ToList());
    }
}
