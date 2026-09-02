namespace DoubleDashScore.Services;

/// <param name="TotalPointsByPlayer">
/// Kvällens totala banpoäng per spelare över <b>alla</b> omgångar, kompletta
/// som partiella — samma underlag som kvällssnittet.
/// </param>
/// <param name="RoundPositions">
/// En post per omgång, partiella inkluderade (se <c>IsComplete</c> på posten).
/// <see cref="PlacementsByPlayer"/> räknar däremot bara kompletta omgångar.
/// </param>
public sealed record NightStats(
    IReadOnlyDictionary<int, decimal> AverageByPlayer,
    IReadOnlyDictionary<int, IReadOnlyList<int>> PlacementsByPlayer,
    IReadOnlyDictionary<int, int> TotalPointsByPlayer,
    IReadOnlyList<RoundPositionsResult> RoundPositions);

/// <param name="IsComplete">
/// Falskt för en partiell omgång. <see cref="PositionByPlayer"/> är då en
/// rangordning på omgångens poäng <b>enbart för visning</b> — den är ingen
/// omgångsplacering och räknas aldrig in i kvällsplaceringar eller totalscore.
/// </param>
public sealed record RoundPositionsResult(
    int RoundId,
    int RoundNumber,
    bool IsComplete,
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
    DateTime? PlayedOnUtc,
    IReadOnlyDictionary<int, decimal> AverageByPlayer)
{
    // Set for historical (imported) nights — null for app-recorded nights.
    // Historical nights have no real date; consumers must NOT invent one.
    public int? HistoricalNightNumber { get; init; }

    // 1-based position in the unified series (historical first by NightNumber,
    // then app by PlayedOn). Used for x-axis placement and as fallback for
    // tooltip labelling when there is no real date.
    public int ChronologicalIndex { get; init; }

    // Per spelare: lista av placeringar för kvällens kompletta omgångar,
    // i omgångsordning. Tom lista om kvällen saknar kompletta omgångar
    // (t.ex. historiska kvällar 1–34 där Excel-källan bara hade aggregat).
    public IReadOnlyDictionary<int, IReadOnlyList<TiedPlacement>> PlacementsByPlayer { get; init; }
        = new Dictionary<int, IReadOnlyList<TiedPlacement>>();

    public bool IsHistorical => HistoricalNightNumber is not null;
}

// En placering (1–4) plus en flagga som markerar att den delades med
// en eller flera andra spelare i samma omgång. Tied placeringar
// renderas med asterisk i UI ("1*").
public sealed record TiedPlacement(int Position, bool IsTied);
