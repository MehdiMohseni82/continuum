using System.Text.Json.Serialization;

namespace Continuum.Core.Domain;

/// <summary>Lifecycle state of a captured Claude Code session.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SessionStatus>))]
public enum SessionStatus
{
    /// <summary>Receiving events; the file is still growing.</summary>
    Live = 0,

    /// <summary>Closed cleanly.</summary>
    Ended = 1,

    /// <summary>Went idle with no clean end — a likely crash or force-quit.</summary>
    Interrupted = 2,

    /// <summary>State could not be determined.</summary>
    Unknown = 3,
}
