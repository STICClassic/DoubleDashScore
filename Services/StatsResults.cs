namespace DoubleDashScore.Services;

public sealed record NightStats(
    IReadOnlyDictionary<int, decimal> AverageByPlayer,
    IReadOnlyDictionary<int, IReadOnlyList<int>> PlacementsByPlayer,
    IReadOnlyList<RoundPositionsResult> CompleteRoundPositions);

public sealed record RoundPositionsResult(
    int RoundId,
    int RoundNumber,
    IReadOnlyDictionary<int, int> PositionByPlayer,
    IReadOnlyDictionary<int, int> TotalPointsByPlayer);

public sealed record HistoryStats(
    PositionTotals PositionTotals,
    IReadOnlyDictionary<int, decimal> CareerAverageByPlayer,
    IReadOnlyList<NightAveragePoint> Series);

public sealed record PositionTotals(IReadOnlyDictionary<int, PositionCounts> ByPlayer);

public sealed record PositionCounts(int Firsts, int Seconds, int Thirds, int Fourths)
{
    public int CompleteRoundsPlayed => Firsts + Seconds + Thirds + Fourths;
}

public sealed record NightAveragePoint(
    DateTime PlayedOnUtc,
    IReadOnlyDictionary<int, decimal> AverageByPlayer);
