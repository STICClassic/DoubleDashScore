using DoubleDashScore.Data;
using DoubleDashScore.Models;

namespace DoubleDashScore.Services;

public static class StatsCalculator
{
    public static NightStats CalculateNightStats(NightWithRounds night, IReadOnlyList<int> activePlayerIds)
    {
        ArgumentNullException.ThrowIfNull(night);
        ArgumentNullException.ThrowIfNull(activePlayerIds);
        if (activePlayerIds.Count != 4)
        {
            throw new ArgumentException("Stats assume exactly 4 active players.", nameof(activePlayerIds));
        }
        if (night.Rounds.Count == 0)
        {
            throw new InvalidOperationException(
                $"Night {night.Night.Id} has no rounds; night stats are undefined.");
        }

        ValidateRoundsHaveAllPlayers(night.Rounds, activePlayerIds, night.Night.Id);

        var totalPoints = activePlayerIds.ToDictionary(id => id, _ => 0);
        var totalTracks = activePlayerIds.ToDictionary(id => id, _ => 0);

        foreach (var round in night.Rounds)
        {
            foreach (var rr in round.Results)
            {
                totalPoints[rr.PlayerId] += PointsFor(rr);
                totalTracks[rr.PlayerId] += TracksFor(rr);
            }
        }

        var averageByPlayer = activePlayerIds.ToDictionary(
            id => id,
            id =>
            {
                var tracks = totalTracks[id];
                if (tracks == 0)
                {
                    throw new InvalidOperationException(
                        $"Player {id} has 0 tracks for night {night.Night.Id}; data corruption suspected.");
                }
                return (decimal)totalPoints[id] / tracks;
            });

        var placementsByPlayer = activePlayerIds.ToDictionary(
            id => id,
            _ => (IReadOnlyList<int>)new List<int>());
        var completeRoundPositions = new List<RoundPositionsResult>();

        foreach (var round in night.Rounds.Where(r => r.IsComplete))
        {
            var positions = CalculateRoundPositions(round, activePlayerIds);
            completeRoundPositions.Add(positions);
            foreach (var id in activePlayerIds)
            {
                ((List<int>)placementsByPlayer[id]).Add(positions.PositionByPlayer[id]);
            }
        }

        return new NightStats(averageByPlayer, placementsByPlayer, completeRoundPositions);
    }

    public static RoundPositionsResult CalculateRoundPositions(RoundDetail round, IReadOnlyList<int> activePlayerIds)
    {
        ArgumentNullException.ThrowIfNull(round);
        ArgumentNullException.ThrowIfNull(activePlayerIds);
        if (!round.IsComplete)
        {
            throw new InvalidOperationException(
                $"Round {round.Round.Id} is not complete (TrackCount={round.Round.TrackCount}, Results={round.Results.Count}); positions undefined.");
        }
        ValidateRoundsHaveAllPlayers(new[] { round }, activePlayerIds, round.Round.GameNightId);

        var totalPoints = round.Results.ToDictionary(r => r.PlayerId, PointsFor);

        var sorted = totalPoints
            .OrderByDescending(kv => kv.Value)
            .ToList();

        var positions = new Dictionary<int, int>();
        int currentRank = 0;
        int prevPoints = int.MinValue;
        for (int i = 0; i < sorted.Count; i++)
        {
            var (playerId, points) = (sorted[i].Key, sorted[i].Value);
            if (i == 0 || points != prevPoints)
            {
                currentRank = i + 1;
            }
            positions[playerId] = currentRank;
            prevPoints = points;
        }

        return new RoundPositionsResult(
            round.Round.Id,
            round.Round.RoundNumber,
            positions,
            totalPoints);
    }

    public static HistoryStats CalculateHistory(
        IReadOnlyList<NightWithRounds> nights,
        IReadOnlyList<int> activePlayerIds)
        => CalculateHistory(nights, activePlayerIds, HistoricalSeed.Empty);

    public static HistoryStats CalculateHistory(
        IReadOnlyList<NightWithRounds> nights,
        IReadOnlyList<int> activePlayerIds,
        HistoricalSeed seed)
    {
        ArgumentNullException.ThrowIfNull(nights);
        ArgumentNullException.ThrowIfNull(activePlayerIds);
        ArgumentNullException.ThrowIfNull(seed);
        if (activePlayerIds.Count != 4)
        {
            throw new ArgumentException("Stats assume exactly 4 active players.", nameof(activePlayerIds));
        }

        var counts = activePlayerIds.ToDictionary(
            id => id,
            _ => (firsts: 0, seconds: 0, thirds: 0, fourths: 0));
        var careerPoints = activePlayerIds.ToDictionary(id => id, _ => 0);
        var careerTracks = activePlayerIds.ToDictionary(id => id, _ => 0);
        var series = new List<NightAveragePoint>();

        ApplySeed(seed, activePlayerIds, counts, careerPoints, careerTracks, series);

        var orderedNights = nights.OrderBy(n => n.Night.PlayedOn).ToList();

        foreach (var night in orderedNights)
        {
            if (night.Rounds.Count == 0)
            {
                continue;
            }

            ValidateRoundsHaveAllPlayers(night.Rounds, activePlayerIds, night.Night.Id);

            var nightPoints = activePlayerIds.ToDictionary(id => id, _ => 0);
            var nightTracks = activePlayerIds.ToDictionary(id => id, _ => 0);

            foreach (var round in night.Rounds)
            {
                foreach (var rr in round.Results)
                {
                    nightPoints[rr.PlayerId] += PointsFor(rr);
                    nightTracks[rr.PlayerId] += TracksFor(rr);
                }

                if (round.IsComplete)
                {
                    var positions = CalculateRoundPositions(round, activePlayerIds);
                    foreach (var id in activePlayerIds)
                    {
                        var (f, s, t, fo) = counts[id];
                        switch (positions.PositionByPlayer[id])
                        {
                            case 1: f++; break;
                            case 2: s++; break;
                            case 3: t++; break;
                            case 4: fo++; break;
                            default:
                                throw new InvalidOperationException(
                                    $"Unexpected position {positions.PositionByPlayer[id]} for player {id} in round {round.Round.Id}.");
                        }
                        counts[id] = (f, s, t, fo);
                    }
                }
            }

            var averageByPlayer = activePlayerIds.ToDictionary(
                id => id,
                id =>
                {
                    var tracks = nightTracks[id];
                    if (tracks == 0)
                    {
                        throw new InvalidOperationException(
                            $"Player {id} has 0 tracks for night {night.Night.Id}; data corruption suspected.");
                    }
                    return (decimal)nightPoints[id] / tracks;
                });

            series.Add(new NightAveragePoint(night.Night.PlayedOn, averageByPlayer));

            foreach (var id in activePlayerIds)
            {
                careerPoints[id] += nightPoints[id];
                careerTracks[id] += nightTracks[id];
            }
        }

        var careerAverageByPlayer = activePlayerIds.ToDictionary(
            id => id,
            id => careerTracks[id] == 0 ? 0m : (decimal)careerPoints[id] / careerTracks[id]);

        var positionTotals = new PositionTotals(activePlayerIds.ToDictionary(
            id => id,
            id =>
            {
                var (f, s, t, fo) = counts[id];
                return new PositionCounts(f, s, t, fo);
            }));

        var indexedSeries = AssignChronologicalIndices(series);
        return new HistoryStats(positionTotals, careerAverageByPlayer, indexedSeries);
    }

    internal static int PointsFor(RoundResult rr) =>
        4 * rr.FirstPlaces + 3 * rr.SecondPlaces + 2 * rr.ThirdPlaces + 1 * rr.FourthPlaces;

    internal static int TracksFor(RoundResult rr) =>
        rr.FirstPlaces + rr.SecondPlaces + rr.ThirdPlaces + rr.FourthPlaces;

    private static int HistoricalPointsFor(HistoricalNightAggregate a) =>
        4 * a.FirstPlaces + 3 * a.SecondPlaces + 2 * a.ThirdPlaces + a.FourthPlaces;

    private static int HistoricalTracksFor(HistoricalNightAggregate a) =>
        a.FirstPlaces + a.SecondPlaces + a.ThirdPlaces + a.FourthPlaces;

    private static void ApplySeed(
        HistoricalSeed seed,
        IReadOnlyList<int> activePlayerIds,
        Dictionary<int, (int firsts, int seconds, int thirds, int fourths)> counts,
        Dictionary<int, int> careerPoints,
        Dictionary<int, int> careerTracks,
        List<NightAveragePoint> series)
    {
        if (seed.IsEmpty) return;

        var activeSet = activePlayerIds.ToHashSet();

        // 1. Position totals start from snapshot.
        foreach (var snap in seed.PositionTotalsSnapshot)
        {
            if (!activeSet.Contains(snap.PlayerId)) continue;
            counts[snap.PlayerId] = (snap.Firsts, snap.Seconds, snap.Thirds, snap.Fourths);
        }

        // 2. Add historical round placements on top.
        foreach (var p in seed.RoundPlacements)
        {
            if (!activeSet.Contains(p.PlayerId)) continue;
            var (f, s, t, fo) = counts[p.PlayerId];
            switch (p.Position)
            {
                case 1: f++; break;
                case 2: s++; break;
                case 3: t++; break;
                case 4: fo++; break;
                default:
                    throw new InvalidOperationException(
                        $"Historisk placering {p.Position} (kväll {p.NightNumber}, spelare {p.PlayerId}) är utanför 1-4.");
            }
            counts[p.PlayerId] = (f, s, t, fo);
        }

        // 3. Career totals accumulate historical aggregates.
        foreach (var agg in seed.NightAggregates)
        {
            if (!activeSet.Contains(agg.PlayerId)) continue;
            careerPoints[agg.PlayerId] += HistoricalPointsFor(agg);
            careerTracks[agg.PlayerId] += HistoricalTracksFor(agg);
        }

        // 4. Series: one point per historical night, ordered ascending by NightNumber.
        //    Each night must have an aggregate for every active player — anything else
        //    is data corruption and should fail loudly.
        var aggsByNight = seed.NightAggregates
            .GroupBy(a => a.NightNumber)
            .OrderBy(g => g.Key);

        foreach (var nightGroup in aggsByNight)
        {
            var aggsByPlayer = nightGroup.ToDictionary(a => a.PlayerId);
            var avgByPlayer = new Dictionary<int, decimal>(activePlayerIds.Count);
            foreach (var id in activePlayerIds)
            {
                if (!aggsByPlayer.TryGetValue(id, out var agg))
                {
                    throw new InvalidOperationException(
                        $"Historisk kväll {nightGroup.Key} saknar aggregat för spelare {id} — datakorruption misstänks.");
                }
                var tracks = HistoricalTracksFor(agg);
                if (tracks == 0)
                {
                    throw new InvalidOperationException(
                        $"Historisk kväll {nightGroup.Key}, spelare {id} har 0 banor — datakorruption misstänks.");
                }
                avgByPlayer[id] = (decimal)HistoricalPointsFor(agg) / tracks;
            }
            series.Add(new NightAveragePoint(PlayedOnUtc: null, avgByPlayer)
            {
                HistoricalNightNumber = nightGroup.Key,
            });
        }
    }

    private static IReadOnlyList<NightAveragePoint> AssignChronologicalIndices(
        IReadOnlyList<NightAveragePoint> series)
    {
        var indexed = new List<NightAveragePoint>(series.Count);
        for (int i = 0; i < series.Count; i++)
        {
            indexed.Add(series[i] with { ChronologicalIndex = i + 1 });
        }
        return indexed;
    }

    private static void ValidateRoundsHaveAllPlayers(
        IReadOnlyList<RoundDetail> rounds,
        IReadOnlyList<int> activePlayerIds,
        int gameNightId)
    {
        var expected = activePlayerIds.ToHashSet();
        foreach (var round in rounds)
        {
            if (round.Results.Count != 4)
            {
                throw new InvalidOperationException(
                    $"Round {round.Round.Id} (night {gameNightId}) has {round.Results.Count} result rows; expected 4. Data corruption suspected.");
            }
            var actual = round.Results.Select(r => r.PlayerId).ToHashSet();
            if (!actual.SetEquals(expected))
            {
                throw new InvalidOperationException(
                    $"Round {round.Round.Id} (night {gameNightId}) result players {{{string.Join(",", actual)}}} do not match active players {{{string.Join(",", expected)}}}. Data corruption suspected.");
            }
        }
    }
}
