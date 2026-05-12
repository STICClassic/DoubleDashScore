using DoubleDashScore.Data;
using DoubleDashScore.Models;
using DoubleDashScore.Services;
using Xunit;

namespace DoubleDashScore.Tests;

public class StatsCalculatorTests
{
    private static readonly int[] Players = { 1, 2, 3, 4 };

    [Fact]
    public void RoundPositions_NoTies_AssignsOneTwoThreeFour()
    {
        var round = MakeRound(
            roundId: 1, gameNightId: 1, roundNumber: 1, trackCount: 16,
            (1, 16, 0, 0, 0),    // 64
            (2, 0, 16, 0, 0),    // 48
            (3, 0, 0, 16, 0),    // 32
            (4, 0, 0, 0, 16));   // 16

        var result = StatsCalculator.CalculateRoundPositions(round, Players);

        Assert.Equal(1, result.PositionByPlayer[1]);
        Assert.Equal(2, result.PositionByPlayer[2]);
        Assert.Equal(3, result.PositionByPlayer[3]);
        Assert.Equal(4, result.PositionByPlayer[4]);
    }

    [Fact]
    public void RoundPositions_TwoTiedAtFirst_AssignsOneOneThreeFour()
    {
        var round = MakeRound(
            roundId: 1, gameNightId: 1, roundNumber: 1, trackCount: 16,
            (1, 8, 8, 0, 0),     // 56
            (2, 8, 8, 0, 0),     // 56
            (3, 0, 0, 16, 0),    // 32
            (4, 0, 0, 0, 16));   // 16

        var result = StatsCalculator.CalculateRoundPositions(round, Players);

        Assert.Equal(1, result.PositionByPlayer[1]);
        Assert.Equal(1, result.PositionByPlayer[2]);
        Assert.Equal(3, result.PositionByPlayer[3]);
        Assert.Equal(4, result.PositionByPlayer[4]);
    }

    [Fact]
    public void RoundPositions_TwoTiedAtSecond_AssignsOneTwoTwoFour()
    {
        var round = MakeRound(
            roundId: 1, gameNightId: 1, roundNumber: 1, trackCount: 16,
            (1, 16, 0, 0, 0),    // 64
            (2, 0, 8, 8, 0),     // 40
            (3, 0, 8, 8, 0),     // 40
            (4, 0, 0, 0, 16));   // 16

        var result = StatsCalculator.CalculateRoundPositions(round, Players);

        Assert.Equal(1, result.PositionByPlayer[1]);
        Assert.Equal(2, result.PositionByPlayer[2]);
        Assert.Equal(2, result.PositionByPlayer[3]);
        Assert.Equal(4, result.PositionByPlayer[4]);
    }

    [Fact]
    public void RoundPositions_ThreeTiedAtFirst_AssignsOneOneOneFour()
    {
        // P1, P2, P3 each: 5+5+5+1 = 16 tracks, 20+15+10+1 = 46 points.
        // P4: 1+1+1+13 = 16 tracks, 4+3+2+13 = 22 points.
        var round = MakeRound(
            roundId: 1, gameNightId: 1, roundNumber: 1, trackCount: 16,
            (1, 5, 5, 5, 1),
            (2, 5, 5, 5, 1),
            (3, 5, 5, 5, 1),
            (4, 1, 1, 1, 13));

        var result = StatsCalculator.CalculateRoundPositions(round, Players);

        Assert.Equal(1, result.PositionByPlayer[1]);
        Assert.Equal(1, result.PositionByPlayer[2]);
        Assert.Equal(1, result.PositionByPlayer[3]);
        Assert.Equal(4, result.PositionByPlayer[4]);
    }

    [Fact]
    public void RoundPositions_TwoPairsOfTies_AssignsOneOneThreeThree()
    {
        // P1, P2 each: 5+5+3+3 = 16, 20+15+6+3 = 44 points.
        // P3, P4 each: 3+3+5+5 = 16, 12+9+10+5 = 36 points.
        var round = MakeRound(
            roundId: 1, gameNightId: 1, roundNumber: 1, trackCount: 16,
            (1, 5, 5, 3, 3),
            (2, 5, 5, 3, 3),
            (3, 3, 3, 5, 5),
            (4, 3, 3, 5, 5));

        var result = StatsCalculator.CalculateRoundPositions(round, Players);

        Assert.Equal(1, result.PositionByPlayer[1]);
        Assert.Equal(1, result.PositionByPlayer[2]);
        Assert.Equal(3, result.PositionByPlayer[3]);
        Assert.Equal(3, result.PositionByPlayer[4]);
    }

    [Fact]
    public void RoundPositions_AllFourTied_AssignsAllOnes()
    {
        // Each player: 4+4+4+4 = 16 tracks, 16+12+8+4 = 40 points.
        var round = MakeRound(
            roundId: 1, gameNightId: 1, roundNumber: 1, trackCount: 16,
            (1, 4, 4, 4, 4),
            (2, 4, 4, 4, 4),
            (3, 4, 4, 4, 4),
            (4, 4, 4, 4, 4));

        var result = StatsCalculator.CalculateRoundPositions(round, Players);

        Assert.Equal(1, result.PositionByPlayer[1]);
        Assert.Equal(1, result.PositionByPlayer[2]);
        Assert.Equal(1, result.PositionByPlayer[3]);
        Assert.Equal(1, result.PositionByPlayer[4]);
    }

    [Fact]
    public void RoundPositions_TotalPointsExposedPerPlayer()
    {
        var round = MakeRound(
            roundId: 1, gameNightId: 1, roundNumber: 1, trackCount: 16,
            (1, 16, 0, 0, 0),    // 64
            (2, 0, 16, 0, 0),    // 48
            (3, 0, 0, 16, 0),    // 32
            (4, 0, 0, 0, 16));   // 16

        var result = StatsCalculator.CalculateRoundPositions(round, Players);

        Assert.Equal(64, result.TotalPointsByPlayer[1]);
        Assert.Equal(48, result.TotalPointsByPlayer[2]);
        Assert.Equal(32, result.TotalPointsByPlayer[3]);
        Assert.Equal(16, result.TotalPointsByPlayer[4]);
    }

    [Fact]
    public void RoundPositions_PartialRound_Throws()
    {
        var round = MakeRound(
            roundId: 1, gameNightId: 1, roundNumber: 1, trackCount: 8,
            (1, 8, 0, 0, 0),
            (2, 0, 8, 0, 0),
            (3, 0, 0, 8, 0),
            (4, 0, 0, 0, 8));

        Assert.Throws<InvalidOperationException>(
            () => StatsCalculator.CalculateRoundPositions(round, Players));
    }

    [Fact]
    public void RoundPositions_MissingPlayer_Throws()
    {
        var round = MakeRound(
            roundId: 1, gameNightId: 1, roundNumber: 1, trackCount: 16,
            (1, 16, 0, 0, 0),
            (2, 0, 16, 0, 0),
            (3, 0, 0, 16, 0),
            (5, 0, 0, 0, 16));

        Assert.Throws<InvalidOperationException>(
            () => StatsCalculator.CalculateRoundPositions(round, Players));
    }

    [Fact]
    public void NightStats_SingleCompleteRound_ComputesAverages()
    {
        var round = MakeRound(
            roundId: 1, gameNightId: 1, roundNumber: 1, trackCount: 16,
            (1, 7, 5, 3, 1),    // 28 + 15 + 6 + 1 = 50 / 16 = 3.125
            (2, 5, 7, 3, 1),    // 20 + 21 + 6 + 1 = 48 / 16 = 3.0
            (3, 3, 3, 7, 3),    // 12 + 9 + 14 + 3 = 38 / 16 = 2.375
            (4, 1, 1, 3, 11));  // 4 + 3 + 6 + 11 = 24 / 16 = 1.5
        var night = new NightWithRounds(MakeNight(1, "2026-01-15"), new[] { round });

        var stats = StatsCalculator.CalculateNightStats(night, Players);

        Assert.Equal(3.125m, stats.AverageByPlayer[1]);
        Assert.Equal(3.0m, stats.AverageByPlayer[2]);
        Assert.Equal(2.375m, stats.AverageByPlayer[3]);
        Assert.Equal(1.5m, stats.AverageByPlayer[4]);
    }

    [Fact]
    public void NightStats_CompletePlusPartial_AveragesAcrossAllTracks()
    {
        // Round 1 (complete, 16): P1 all firsts → 64. Round 2 (partial, 4): P1 all firsts → 16.
        // P1: 80 / 20 = 4.0.
        var r1 = MakeRound(
            roundId: 1, gameNightId: 1, roundNumber: 1, trackCount: 16,
            (1, 16, 0, 0, 0),
            (2, 0, 16, 0, 0),
            (3, 0, 0, 16, 0),
            (4, 0, 0, 0, 16));
        var r2 = MakeRound(
            roundId: 2, gameNightId: 1, roundNumber: 2, trackCount: 4,
            (1, 4, 0, 0, 0),
            (2, 0, 4, 0, 0),
            (3, 0, 0, 4, 0),
            (4, 0, 0, 0, 4));
        var night = new NightWithRounds(MakeNight(1, "2026-01-15"), new[] { r1, r2 });

        var stats = StatsCalculator.CalculateNightStats(night, Players);

        Assert.Equal(4.0m, stats.AverageByPlayer[1]);
        Assert.Equal(3.0m, stats.AverageByPlayer[2]);
        Assert.Equal(2.0m, stats.AverageByPlayer[3]);
        Assert.Equal(1.0m, stats.AverageByPlayer[4]);
    }

    [Fact]
    public void NightStats_CollectsPlacementsAcrossCompleteRoundsOnly()
    {
        // r1 (complete): P1 wins outright.
        // r2 (complete): P1 and P2 tie at first.
        // r3 (partial): not counted.
        var r1 = MakeRound(
            1, 1, 1, 16,
            (1, 16, 0, 0, 0),
            (2, 0, 16, 0, 0),
            (3, 0, 0, 16, 0),
            (4, 0, 0, 0, 16));
        var r2 = MakeRound(
            2, 1, 2, 16,
            (1, 8, 8, 0, 0),
            (2, 8, 8, 0, 0),
            (3, 0, 0, 16, 0),
            (4, 0, 0, 0, 16));
        var r3 = MakeRound(
            3, 1, 3, 4,
            (1, 4, 0, 0, 0),
            (2, 0, 4, 0, 0),
            (3, 0, 0, 4, 0),
            (4, 0, 0, 0, 4));
        var night = new NightWithRounds(MakeNight(1, "2026-01-15"), new[] { r1, r2, r3 });

        var stats = StatsCalculator.CalculateNightStats(night, Players);

        Assert.Equal(new[] { 1, 1 }, stats.PlacementsByPlayer[1]);
        Assert.Equal(new[] { 2, 1 }, stats.PlacementsByPlayer[2]);
        Assert.Equal(new[] { 3, 3 }, stats.PlacementsByPlayer[3]);
        Assert.Equal(new[] { 4, 4 }, stats.PlacementsByPlayer[4]);
        Assert.Equal(2, stats.CompleteRoundPositions.Count);
    }

    [Fact]
    public void NightStats_EmptyNight_Throws()
    {
        var night = new NightWithRounds(MakeNight(1, "2026-01-15"), Array.Empty<RoundDetail>());

        Assert.Throws<InvalidOperationException>(
            () => StatsCalculator.CalculateNightStats(night, Players));
    }

    [Fact]
    public void NightStats_RoundMissingPlayer_Throws()
    {
        var round = MakeRound(
            1, 1, 1, 16,
            (1, 16, 0, 0, 0),
            (2, 0, 16, 0, 0),
            (3, 0, 0, 16, 0),
            (5, 0, 0, 0, 16));
        var night = new NightWithRounds(MakeNight(1, "2026-01-15"), new[] { round });

        Assert.Throws<InvalidOperationException>(
            () => StatsCalculator.CalculateNightStats(night, Players));
    }

    [Fact]
    public void History_ThreeTiedAtFirst_AllGetFirstsCounter()
    {
        // P1=P2=P3 each 46 points, P4 22. Position counts: P1/P2/P3 +1 firsts, P4 +1 fourths.
        var round = MakeRound(
            1, 1, 1, 16,
            (1, 5, 5, 5, 1),
            (2, 5, 5, 5, 1),
            (3, 5, 5, 5, 1),
            (4, 1, 1, 1, 13));
        var night = new NightWithRounds(MakeNight(1, "2026-01-15"), new[] { round });

        var history = StatsCalculator.CalculateHistory(new[] { night }, Players);

        Assert.Equal(new PositionCounts(1, 0, 0, 0), history.PositionTotals.ByPlayer[1]);
        Assert.Equal(new PositionCounts(1, 0, 0, 0), history.PositionTotals.ByPlayer[2]);
        Assert.Equal(new PositionCounts(1, 0, 0, 0), history.PositionTotals.ByPlayer[3]);
        Assert.Equal(new PositionCounts(0, 0, 0, 1), history.PositionTotals.ByPlayer[4]);
    }

    [Fact]
    public void History_PartialRoundsDoNotIncrementPositionTotals()
    {
        var partial = MakeRound(
            1, 1, 1, 4,
            (1, 4, 0, 0, 0),
            (2, 0, 4, 0, 0),
            (3, 0, 0, 4, 0),
            (4, 0, 0, 0, 4));
        var night = new NightWithRounds(MakeNight(1, "2026-01-15"), new[] { partial });

        var history = StatsCalculator.CalculateHistory(new[] { night }, Players);

        foreach (var id in Players)
        {
            Assert.Equal(new PositionCounts(0, 0, 0, 0), history.PositionTotals.ByPlayer[id]);
        }
    }

    [Fact]
    public void History_CareerAverage_AcrossCompleteAndPartialRounds()
    {
        // Night 1, complete (16): P1 = 50 points (7+5+3+1 layout from earlier test).
        // Night 2, partial (4): P1 = 16 points (4 firsts).
        // Career: (50 + 16) / (16 + 4) = 66 / 20 = 3.3
        var r1 = MakeRound(
            1, 1, 1, 16,
            (1, 7, 5, 3, 1),
            (2, 5, 7, 3, 1),
            (3, 3, 3, 7, 3),
            (4, 1, 1, 3, 11));
        var r2 = MakeRound(
            2, 2, 1, 4,
            (1, 4, 0, 0, 0),
            (2, 0, 4, 0, 0),
            (3, 0, 0, 4, 0),
            (4, 0, 0, 0, 4));
        var n1 = new NightWithRounds(MakeNight(1, "2026-01-15"), new[] { r1 });
        var n2 = new NightWithRounds(MakeNight(2, "2026-01-22"), new[] { r2 });

        var history = StatsCalculator.CalculateHistory(new[] { n1, n2 }, Players);

        Assert.Equal(3.3m, history.CareerAverageByPlayer[1]);
    }

    [Fact]
    public void History_SeriesIsSortedAscendingByPlayedOn()
    {
        var r1 = MakeRound(
            1, 1, 1, 16,
            (1, 16, 0, 0, 0),
            (2, 0, 16, 0, 0),
            (3, 0, 0, 16, 0),
            (4, 0, 0, 0, 16));
        var r2 = MakeRound(
            2, 2, 1, 16,
            (1, 16, 0, 0, 0),
            (2, 0, 16, 0, 0),
            (3, 0, 0, 16, 0),
            (4, 0, 0, 0, 16));
        var jan = new NightWithRounds(MakeNight(1, "2026-01-15"), new[] { r1 });
        var feb = new NightWithRounds(MakeNight(2, "2026-02-15"), new[] { r2 });

        var history = StatsCalculator.CalculateHistory(new[] { feb, jan }, Players);

        Assert.Equal(2, history.Series.Count);
        Assert.True(history.Series[0].PlayedOnUtc < history.Series[1].PlayedOnUtc);
    }

    [Fact]
    public void History_EmptyNightsSkippedFromSeries()
    {
        var emptyNight = new NightWithRounds(MakeNight(1, "2026-01-15"), Array.Empty<RoundDetail>());
        var round = MakeRound(
            1, 2, 1, 16,
            (1, 16, 0, 0, 0),
            (2, 0, 16, 0, 0),
            (3, 0, 0, 16, 0),
            (4, 0, 0, 0, 16));
        var realNight = new NightWithRounds(MakeNight(2, "2026-01-22"), new[] { round });

        var history = StatsCalculator.CalculateHistory(new[] { emptyNight, realNight }, Players);

        Assert.Single(history.Series);
        Assert.Equal(realNight.Night.PlayedOn, history.Series[0].PlayedOnUtc);
    }

    [Fact]
    public void History_NoNights_ReturnsZeroCareerAndEmptySeries()
    {
        var history = StatsCalculator.CalculateHistory(Array.Empty<NightWithRounds>(), Players);

        Assert.Empty(history.Series);
        foreach (var id in Players)
        {
            Assert.Equal(0m, history.CareerAverageByPlayer[id]);
            Assert.Equal(new PositionCounts(0, 0, 0, 0), history.PositionTotals.ByPlayer[id]);
        }
    }

    [Fact]
    public void History_SnapshotInSeed_BecomesStartingPositionTotals()
    {
        var seed = new HistoricalSeed(
            NightAggregates: Array.Empty<HistoricalNightAggregate>(),
            RoundPlacements: Array.Empty<HistoricalRoundPlacement>(),
            PositionTotalsSnapshot: new[]
            {
                new HistoricalPositionTotalsSnapshot { PlayerId = 1, Firsts = 58, Seconds = 12, Thirds = 10, Fourths = 2, CreatedAt = DateTime.UtcNow },
                new HistoricalPositionTotalsSnapshot { PlayerId = 2, Firsts = 0,  Seconds = 7,  Thirds = 33, Fourths = 42, CreatedAt = DateTime.UtcNow },
                new HistoricalPositionTotalsSnapshot { PlayerId = 3, Firsts = 25, Seconds = 43, Thirds = 10, Fourths = 4,  CreatedAt = DateTime.UtcNow },
                new HistoricalPositionTotalsSnapshot { PlayerId = 4, Firsts = 5,  Seconds = 17, Thirds = 29, Fourths = 28, CreatedAt = DateTime.UtcNow },
            });

        var history = StatsCalculator.CalculateHistory(Array.Empty<NightWithRounds>(), Players, seed);

        Assert.Equal(new PositionCounts(58, 12, 10, 2), history.PositionTotals.ByPlayer[1]);
        Assert.Equal(new PositionCounts(0, 7, 33, 42), history.PositionTotals.ByPlayer[2]);
    }

    [Fact]
    public void History_PlacementsInSeed_AddedOnTopOfSnapshot()
    {
        var seed = new HistoricalSeed(
            NightAggregates: Array.Empty<HistoricalNightAggregate>(),
            RoundPlacements: new[]
            {
                // Claes wins two historical rounds, comes 4th once.
                new HistoricalRoundPlacement { NightNumber = 1, PlayerId = 1, RoundIndex = 1, Position = 1, CreatedAt = DateTime.UtcNow },
                new HistoricalRoundPlacement { NightNumber = 2, PlayerId = 1, RoundIndex = 1, Position = 1, CreatedAt = DateTime.UtcNow },
                new HistoricalRoundPlacement { NightNumber = 3, PlayerId = 1, RoundIndex = 1, Position = 4, CreatedAt = DateTime.UtcNow },
            },
            PositionTotalsSnapshot: new[]
            {
                new HistoricalPositionTotalsSnapshot { PlayerId = 1, Firsts = 10, Seconds = 0, Thirds = 0, Fourths = 0, CreatedAt = DateTime.UtcNow },
            });

        var history = StatsCalculator.CalculateHistory(Array.Empty<NightWithRounds>(), Players, seed);

        // Snapshot 10 firsts + 2 from placements = 12. Plus 1 fourth from placements.
        Assert.Equal(new PositionCounts(12, 0, 0, 1), history.PositionTotals.ByPlayer[1]);
    }

    [Fact]
    public void History_HistoricalAggregates_AddToCareerAverage()
    {
        // Historical: P1 = (4×7 + 3×5 + 2×3 + 1×1) = 50 points over 16 tracks.
        // App night: P1 = 4 firsts × 4 = 16 points over 4 tracks (partial round).
        // Combined career: (50 + 16) / (16 + 4) = 66 / 20 = 3.3
        var seed = new HistoricalSeed(
            NightAggregates: new[]
            {
                new HistoricalNightAggregate { NightNumber = 1, PlayerId = 1, FirstPlaces = 7, SecondPlaces = 5, ThirdPlaces = 3, FourthPlaces = 1, TotalTracks = 16, CreatedAt = DateTime.UtcNow },
                new HistoricalNightAggregate { NightNumber = 1, PlayerId = 2, FirstPlaces = 5, SecondPlaces = 7, ThirdPlaces = 3, FourthPlaces = 1, TotalTracks = 16, CreatedAt = DateTime.UtcNow },
                new HistoricalNightAggregate { NightNumber = 1, PlayerId = 3, FirstPlaces = 3, SecondPlaces = 3, ThirdPlaces = 7, FourthPlaces = 3, TotalTracks = 16, CreatedAt = DateTime.UtcNow },
                new HistoricalNightAggregate { NightNumber = 1, PlayerId = 4, FirstPlaces = 1, SecondPlaces = 1, ThirdPlaces = 3, FourthPlaces = 11, TotalTracks = 16, CreatedAt = DateTime.UtcNow },
            },
            RoundPlacements: Array.Empty<HistoricalRoundPlacement>(),
            PositionTotalsSnapshot: Array.Empty<HistoricalPositionTotalsSnapshot>());

        var partial = MakeRound(1, 1, 1, 4,
            (1, 4, 0, 0, 0),
            (2, 0, 4, 0, 0),
            (3, 0, 0, 4, 0),
            (4, 0, 0, 0, 4));
        var night = new NightWithRounds(MakeNight(1, "2026-01-15"), new[] { partial });

        var history = StatsCalculator.CalculateHistory(new[] { night }, Players, seed);

        Assert.Equal(3.3m, history.CareerAverageByPlayer[1]);
    }

    [Fact]
    public void History_Series_HistoricalPointsComeBeforeAppPointsAndAreOrderedByNightNumber()
    {
        var seed = new HistoricalSeed(
            NightAggregates: new[]
            {
                // Night 2 added before night 1 in the input to verify ordering by NightNumber.
                new HistoricalNightAggregate { NightNumber = 2, PlayerId = 1, FirstPlaces = 16, SecondPlaces = 0, ThirdPlaces = 0, FourthPlaces = 0, TotalTracks = 16, CreatedAt = DateTime.UtcNow },
                new HistoricalNightAggregate { NightNumber = 2, PlayerId = 2, FirstPlaces = 0, SecondPlaces = 16, ThirdPlaces = 0, FourthPlaces = 0, TotalTracks = 16, CreatedAt = DateTime.UtcNow },
                new HistoricalNightAggregate { NightNumber = 2, PlayerId = 3, FirstPlaces = 0, SecondPlaces = 0, ThirdPlaces = 16, FourthPlaces = 0, TotalTracks = 16, CreatedAt = DateTime.UtcNow },
                new HistoricalNightAggregate { NightNumber = 2, PlayerId = 4, FirstPlaces = 0, SecondPlaces = 0, ThirdPlaces = 0, FourthPlaces = 16, TotalTracks = 16, CreatedAt = DateTime.UtcNow },
                new HistoricalNightAggregate { NightNumber = 1, PlayerId = 1, FirstPlaces = 0, SecondPlaces = 16, ThirdPlaces = 0, FourthPlaces = 0, TotalTracks = 16, CreatedAt = DateTime.UtcNow },
                new HistoricalNightAggregate { NightNumber = 1, PlayerId = 2, FirstPlaces = 16, SecondPlaces = 0, ThirdPlaces = 0, FourthPlaces = 0, TotalTracks = 16, CreatedAt = DateTime.UtcNow },
                new HistoricalNightAggregate { NightNumber = 1, PlayerId = 3, FirstPlaces = 0, SecondPlaces = 0, ThirdPlaces = 16, FourthPlaces = 0, TotalTracks = 16, CreatedAt = DateTime.UtcNow },
                new HistoricalNightAggregate { NightNumber = 1, PlayerId = 4, FirstPlaces = 0, SecondPlaces = 0, ThirdPlaces = 0, FourthPlaces = 16, TotalTracks = 16, CreatedAt = DateTime.UtcNow },
            },
            RoundPlacements: Array.Empty<HistoricalRoundPlacement>(),
            PositionTotalsSnapshot: Array.Empty<HistoricalPositionTotalsSnapshot>());

        var appRound = MakeRound(1, 1, 1, 16,
            (1, 16, 0, 0, 0), (2, 0, 16, 0, 0), (3, 0, 0, 16, 0), (4, 0, 0, 0, 16));
        var appNight = new NightWithRounds(MakeNight(1, "2026-05-01"), new[] { appRound });

        var history = StatsCalculator.CalculateHistory(new[] { appNight }, Players, seed);

        Assert.Equal(3, history.Series.Count);
        Assert.Equal(1, history.Series[0].HistoricalNightNumber);
        Assert.Equal(2, history.Series[1].HistoricalNightNumber);
        Assert.Null(history.Series[2].HistoricalNightNumber);
        // P1 averages: night 1 historical = 3 (all seconds), night 2 historical = 4 (all firsts), app = 4.
        Assert.Equal(3m, history.Series[0].AverageByPlayer[1]);
        Assert.Equal(4m, history.Series[1].AverageByPlayer[1]);
        Assert.Equal(4m, history.Series[2].AverageByPlayer[1]);
    }

    [Fact]
    public void History_HistoricalNightMissingPlayerAggregate_Throws()
    {
        // Night 1 has only 3 of 4 players → corruption signal.
        var seed = new HistoricalSeed(
            NightAggregates: new[]
            {
                new HistoricalNightAggregate { NightNumber = 1, PlayerId = 1, FirstPlaces = 16, SecondPlaces = 0, ThirdPlaces = 0, FourthPlaces = 0, TotalTracks = 16, CreatedAt = DateTime.UtcNow },
                new HistoricalNightAggregate { NightNumber = 1, PlayerId = 2, FirstPlaces = 0, SecondPlaces = 16, ThirdPlaces = 0, FourthPlaces = 0, TotalTracks = 16, CreatedAt = DateTime.UtcNow },
                new HistoricalNightAggregate { NightNumber = 1, PlayerId = 3, FirstPlaces = 0, SecondPlaces = 0, ThirdPlaces = 16, FourthPlaces = 0, TotalTracks = 16, CreatedAt = DateTime.UtcNow },
                // PlayerId 4 missing
            },
            RoundPlacements: Array.Empty<HistoricalRoundPlacement>(),
            PositionTotalsSnapshot: Array.Empty<HistoricalPositionTotalsSnapshot>());

        var ex = Assert.Throws<InvalidOperationException>(
            () => StatsCalculator.CalculateHistory(Array.Empty<NightWithRounds>(), Players, seed));
        Assert.Contains("spelare 4", ex.Message);
    }

    private static RoundDetail MakeRound(
        int roundId,
        int gameNightId,
        int roundNumber,
        int trackCount,
        params (int playerId, int f, int s, int t, int fo)[] results)
    {
        var round = new Round
        {
            Id = roundId,
            GameNightId = gameNightId,
            RoundNumber = roundNumber,
            TrackCount = trackCount,
            CreatedAt = DateTime.UtcNow,
        };
        var rrs = results.Select((r, idx) => new RoundResult
        {
            Id = roundId * 100 + idx,
            RoundId = roundId,
            PlayerId = r.playerId,
            FirstPlaces = r.f,
            SecondPlaces = r.s,
            ThirdPlaces = r.t,
            FourthPlaces = r.fo,
            CreatedAt = DateTime.UtcNow,
        }).ToList();
        return new RoundDetail(round, rrs);
    }

    private static GameNight MakeNight(int id, string isoDate) => new()
    {
        Id = id,
        PlayedOn = DateTime.SpecifyKind(DateTime.Parse(isoDate), DateTimeKind.Utc),
        CreatedAt = DateTime.UtcNow,
    };
}
