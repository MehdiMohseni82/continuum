namespace Continuum.Core.Contracts;

/// <summary>A single item in the live activity feed shown in the header bell.</summary>
public sealed record NotificationDto(
    string Id,          // stable per-item id, e.g. "msg-42" / "handoff-<guid>"
    string Kind,        // message | handoff
    string Title,       // headline, e.g. "researcher → implementer"
    string Detail,      // body snippet / hand-off title
    DateTimeOffset Timestamp,
    string Severity);   // info | warning
