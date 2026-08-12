using System.Text.Json.Serialization;

namespace Continuum.Core.Domain;

/// <summary>Kind of a durable memory, mirroring Claude Code's file-based memory categories.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<MemoryType>))]
public enum MemoryType
{
    /// <summary>Who the user is — role, expertise, preferences.</summary>
    User = 0,

    /// <summary>Guidance on how to work — corrections and confirmed approaches.</summary>
    Feedback = 1,

    /// <summary>Ongoing work, goals, constraints not derivable from the code.</summary>
    Project = 2,

    /// <summary>Pointers to external resources (URLs, dashboards, tickets).</summary>
    Reference = 3,
}
