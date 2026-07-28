namespace Continuum.Core.Domain;

/// <summary>
/// The daemon's memory of how far it has read a given file. Persisted daemon-side
/// (local SQLite) rather than in the backend, so tailing resumes even while offline.
/// The byte offset is advanced only after the server acknowledges the batch.
/// </summary>
public class FileCursor
{
    public required string FilePath { get; set; }

    /// <summary>Source session id derived from the file name.</summary>
    public Guid SessionId { get; set; }

    /// <summary>Project directory name the file lives under.</summary>
    public required string ProjectKey { get; set; }

    public long ByteOffset { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
