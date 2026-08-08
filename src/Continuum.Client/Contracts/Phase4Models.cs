namespace Continuum.Core.Contracts;

public sealed record AnalyticsDto(
    int Sessions,
    int Events,
    int Memories,
    int Agents,
    int Handoffs,
    IReadOnlyList<CountByLabel> SessionsByMachine,
    IReadOnlyList<CountByLabel> SessionsByStatus,
    IReadOnlyList<CountByLabel> TopWorkspaces,
    IReadOnlyList<CountByLabel> MemoriesByType,
    IReadOnlyList<CountByLabel> EventsPerDay);

public sealed record CountByLabel(string Label, int Count);

public sealed record RedactionHitDto(
    Guid SessionId, string? SessionTitle, long EventId, IReadOnlyList<string> Labels, string Snippet);

public sealed record MaintenanceResult(string Operation, int Affected);
