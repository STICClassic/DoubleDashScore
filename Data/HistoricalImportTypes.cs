using DoubleDashScore.Models;

namespace DoubleDashScore.Data;

public sealed record ParsedExcelImport(
    IReadOnlyList<string> PlayerNamesInExcelColumnOrder,
    IReadOnlyList<HistoricalNightAggregate> NightAggregates,
    IReadOnlyList<HistoricalRoundPlacement> RoundPlacements,
    IReadOnlyList<HistoricalPositionTotalsSnapshot> PositionTotalsSnapshot);

public sealed record ImportPreview(
    int TotalNightsInFile,
    int NewNightsToImport,
    int NewPlacementsToImport,
    IReadOnlyList<int> MissingNightNumbersInFile,
    bool SnapshotWillBeReplaced,
    IReadOnlyList<string> PlayerNamesInExcelColumnOrder);

public sealed record ImportResult(int NightsInserted, int PlacementsInserted, bool SnapshotReplaced);
