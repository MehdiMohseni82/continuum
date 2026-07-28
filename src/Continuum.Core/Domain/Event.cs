using NpgsqlTypes;

namespace Continuum.Core.Domain;

/// <summary>
/// A single append-only transcript line. The raw JSON is always kept verbatim (jsonb),
/// so an unrecognized line from a newer Claude Code version still lands intact and searchable.
/// </summary>
public class Event
{
    /// <summary>Server-assigned sequence.</summary>
    public long Id { get; set; }

    public Guid SessionId { get; set; }
    public Session? Session { get; set; }

    /// <summary>Line uuid; synthesized deterministically when the source line has none. Unique with SessionId.</summary>
    public Guid Uuid { get; set; }

    public Guid? ParentUuid { get; set; }

    /// <summary>Raw line type (user | assistant | system | tool_result | ai-title | ...). Never trusted to be from a fixed set.</summary>
    public required string Type { get; set; }

    public string? Role { get; set; }

    public DateTimeOffset Timestamp { get; set; }

    /// <summary>Flattened text used for display and full-text search.</summary>
    public string? TextExcerpt { get; set; }

    /// <summary>The full original line, stored as jsonb.</summary>
    public required string RawJson { get; set; }

    /// <summary>Claude Code version tag carried on the ingest batch, for tolerating format drift.</summary>
    public string? CcVersion { get; set; }

    /// <summary>Generated tsvector over <see cref="TextExcerpt"/> (configured in the DbContext).</summary>
    public NpgsqlTsVector? SearchVector { get; set; }
}
