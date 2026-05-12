using DoubleDashScore.Models;

namespace DoubleDashScore.Services;

/// Bundles the three historical datasets imported from Excel (Skiva 4 PR-A).
/// Used as input to StatsCalculator/CsvBuilder so history + app data can be
/// combined without each consumer re-loading from the repository.
public sealed record HistoricalSeed(
    IReadOnlyList<HistoricalNightAggregate> NightAggregates,
    IReadOnlyList<HistoricalRoundPlacement> RoundPlacements,
    IReadOnlyList<HistoricalPositionTotalsSnapshot> PositionTotalsSnapshot)
{
    public static HistoricalSeed Empty { get; } = new(
        Array.Empty<HistoricalNightAggregate>(),
        Array.Empty<HistoricalRoundPlacement>(),
        Array.Empty<HistoricalPositionTotalsSnapshot>());

    public bool IsEmpty =>
        NightAggregates.Count == 0 &&
        RoundPlacements.Count == 0 &&
        PositionTotalsSnapshot.Count == 0;
}
