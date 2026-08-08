namespace Continuum.Core.Contracts;

public sealed record ModelUsage(
    string Model, long Input, long Output, long CacheRead, long CacheWrite, double CostUsd);

public sealed record LabeledCost(string Label, double CostUsd, long Tokens);

public sealed record TokenStatsDto(
    long TotalInput,
    long TotalOutput,
    long TotalCacheRead,
    long TotalCacheWrite,
    double EstimatedCostUsd,
    IReadOnlyList<ModelUsage> ByModel,
    IReadOnlyList<LabeledCost> ByProject,
    IReadOnlyList<LabeledCost> PerDay);
