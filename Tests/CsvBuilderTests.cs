using DoubleDashScore.Data;
using DoubleDashScore.Models;
using DoubleDashScore.Services;
using Xunit;

namespace DoubleDashScore.Tests;

public class CsvBuilderTests
{
    private static readonly IReadOnlyList<Player> FourPlayers = new[]
    {
        new Player { Id = 1, Name = "Claes", DisplayOrder = 0, CreatedAt = DateTime.UtcNow },
        new Player { Id = 2, Name = "Robin", DisplayOrder = 1, CreatedAt = DateTime.UtcNow },
        new Player { Id = 3, Name = "Aleksi", DisplayOrder = 2, CreatedAt = DateTime.UtcNow },
        new Player { Id = 4, Name = "Jonas", DisplayOrder = 3, CreatedAt = DateTime.UtcNow },
    };

    [Fact]
    public void Section1_HeaderHasNightNumberOnFirstRowAndNamesPlusTotalOnSecond()
    {
        var night = new NightWithRounds(MakeNight(1, "2026-01-15"), new[]
        {
            MakeRound(1, 1, 1, 16,
                (1, 16, 0, 0, 0),
                (2, 0, 16, 0, 0),
                (3, 0, 0, 16, 0),
                (4, 0, 0, 0, 16)),
        });

        var lines = CsvBuilder.BuildHistoryCsv(new[] { night }, FourPlayers).Split('\n');

        Assert.Equal("Kväll 1;2026-01-15", lines[0]);
        Assert.Equal(";Claes;Robin;Aleksi;Jonas;16", lines[1]);
    }

    [Fact]
    public void Section1_TotalTracksSumsAcrossAllRoundsInNight()
    {
        // Två omgångar: 16 + 4 = 20 banor
        var complete = MakeRound(1, 1, 1, 16,
            (1, 16, 0, 0, 0),
            (2, 0, 16, 0, 0),
            (3, 0, 0, 16, 0),
            (4, 0, 0, 0, 16));
        var partial = MakeRound(2, 1, 2, 4,
            (1, 4, 0, 0, 0),
            (2, 0, 4, 0, 0),
            (3, 0, 0, 4, 0),
            (4, 0, 0, 0, 4));
        var night = new NightWithRounds(MakeNight(1, "2026-01-15"), new[] { complete, partial });

        var nameRow = CsvBuilder.BuildHistoryCsv(new[] { night }, FourPlayers).Split('\n')[1];

        Assert.EndsWith(";20", nameRow);
    }

    [Fact]
    public void Section1_PositionRowsSumCountsAcrossRounds()
    {
        var r1 = MakeRound(1, 1, 1, 16,
            (1, 7, 5, 3, 1),
            (2, 5, 7, 3, 1),
            (3, 3, 3, 7, 3),
            (4, 1, 1, 3, 11));
        var r2 = MakeRound(2, 1, 2, 4,
            (1, 4, 0, 0, 0),
            (2, 0, 4, 0, 0),
            (3, 0, 0, 4, 0),
            (4, 0, 0, 0, 4));
        var night = new NightWithRounds(MakeNight(1, "2026-01-15"), new[] { r1, r2 });

        var lines = CsvBuilder.BuildHistoryCsv(new[] { night }, FourPlayers).Split('\n');

        // 1:or per spelare: 7+4=11, 5+0=5, 3+0=3, 1+0=1
        Assert.Equal("1;11;5;3;1", lines[2]);
        // 2:or: 5+0=5, 7+4=11, 3+0=3, 1+0=1
        Assert.Equal("2;5;11;3;1", lines[3]);
        // 3:or: 3+0=3, 3+0=3, 7+4=11, 3+0=3
        Assert.Equal("3;3;3;11;3", lines[4]);
        // 4:or: 1+0=1, 1+0=1, 3+0=3, 11+4=15
        Assert.Equal("4;1;1;3;15", lines[5]);
    }

    [Fact]
    public void Section1_PointsAndSnittComputedAcrossAllRoundsIncludingPartial()
    {
        // 16 banor + 4 banor = 20 totalt.
        // Claes: r1 (7+5+3+1)·(4+3+2+1) = 28+15+6+1 = 50; r2 (4·4) = 16 → 66 poäng / 20 banor = 3,30
        var r1 = MakeRound(1, 1, 1, 16,
            (1, 7, 5, 3, 1),
            (2, 5, 7, 3, 1),
            (3, 3, 3, 7, 3),
            (4, 1, 1, 3, 11));
        var r2 = MakeRound(2, 1, 2, 4,
            (1, 4, 0, 0, 0),
            (2, 0, 4, 0, 0),
            (3, 0, 0, 4, 0),
            (4, 0, 0, 0, 4));
        var night = new NightWithRounds(MakeNight(1, "2026-01-15"), new[] { r1, r2 });

        var lines = CsvBuilder.BuildHistoryCsv(new[] { night }, FourPlayers).Split('\n');

        Assert.StartsWith("Poäng;66;", lines[6]); // Claes
        Assert.StartsWith("Snitt;3,30;", lines[7]); // sv-SE komma, 2 decimaler
    }

    [Fact]
    public void Section1_NightsSortAscendingAndNumberedChronologically()
    {
        var jan = new NightWithRounds(MakeNight(1, "2026-01-15"), new[]
        {
            MakeRound(1, 1, 1, 16,
                (1, 16, 0, 0, 0), (2, 0, 16, 0, 0), (3, 0, 0, 16, 0), (4, 0, 0, 0, 16))
        });
        var feb = new NightWithRounds(MakeNight(2, "2026-02-15"), new[]
        {
            MakeRound(2, 2, 1, 16,
                (1, 16, 0, 0, 0), (2, 0, 16, 0, 0), (3, 0, 0, 16, 0), (4, 0, 0, 0, 16))
        });

        // Skickas i fel ordning — feb först, jan sist.
        var csv = CsvBuilder.BuildHistoryCsv(new[] { feb, jan }, FourPlayers);
        var lines = csv.Split('\n');

        Assert.StartsWith("Kväll 1;2026-01-15", lines[0]);
        // Kvällsblock 1 = 8 rader (Kväll-rad + namnrad + 4 + Poäng + Snitt) + 1 blank = 9 rader → nästa block börjar på index 9.
        Assert.StartsWith("Kväll 2;2026-02-15", lines[9]);
    }

    [Fact]
    public void Section1_BlocksSeparatedByBlankLine()
    {
        var n1 = new NightWithRounds(MakeNight(1, "2026-01-15"), new[]
        {
            MakeRound(1, 1, 1, 16,
                (1, 16, 0, 0, 0), (2, 0, 16, 0, 0), (3, 0, 0, 16, 0), (4, 0, 0, 0, 16))
        });
        var n2 = new NightWithRounds(MakeNight(2, "2026-02-15"), new[]
        {
            MakeRound(2, 2, 1, 16,
                (1, 16, 0, 0, 0), (2, 0, 16, 0, 0), (3, 0, 0, 16, 0), (4, 0, 0, 0, 16))
        });

        var lines = CsvBuilder.BuildHistoryCsv(new[] { n1, n2 }, FourPlayers).Split('\n');

        // Block 1: rader 0–7 (Kväll-rad, namnrad, 4 räknarrader, Poäng, Snitt). Blank rad: 8. Block 2: rader 9–16.
        Assert.Equal(string.Empty, lines[8]);
    }

    [Fact]
    public void Section2_HeaderListsAllNightsAndOneRowPerPlayer()
    {
        var n1 = new NightWithRounds(MakeNight(1, "2026-01-15"), new[]
        {
            MakeRound(1, 1, 1, 16,
                (1, 16, 0, 0, 0), (2, 0, 16, 0, 0), (3, 0, 0, 16, 0), (4, 0, 0, 0, 16))
        });
        var n2 = new NightWithRounds(MakeNight(2, "2026-02-15"), new[]
        {
            MakeRound(2, 2, 1, 16,
                (1, 16, 0, 0, 0), (2, 0, 16, 0, 0), (3, 0, 0, 16, 0), (4, 0, 0, 0, 16))
        });

        var section2 = ExtractPlacements(CsvBuilder.BuildHistoryCsv(new[] { n1, n2 }, FourPlayers));

        Assert.Equal("Spelare;Kväll 1;Kväll 2", section2[0]);
        Assert.StartsWith("Claes;", section2[1]);
        Assert.StartsWith("Robin;", section2[2]);
        Assert.StartsWith("Aleksi;", section2[3]);
        Assert.StartsWith("Jonas;", section2[4]);
    }

    [Fact]
    public void Section2_MultipleCompleteRounds_CommaSeparatedNoSpace()
    {
        // Två kompletta omgångar i samma kväll, Claes vinner båda (placering 1, 1)
        var r1 = MakeRound(1, 1, 1, 16,
            (1, 16, 0, 0, 0), (2, 0, 16, 0, 0), (3, 0, 0, 16, 0), (4, 0, 0, 0, 16));
        var r2 = MakeRound(2, 1, 2, 16,
            (1, 16, 0, 0, 0), (2, 0, 16, 0, 0), (3, 0, 0, 16, 0), (4, 0, 0, 0, 16));
        var night = new NightWithRounds(MakeNight(1, "2026-01-15"), new[] { r1, r2 });

        var section2 = ExtractPlacements(CsvBuilder.BuildHistoryCsv(new[] { night }, FourPlayers));

        Assert.Equal("Claes;1,1", section2[1]);
        Assert.Equal("Robin;2,2", section2[2]);
    }

    [Fact]
    public void Section2_NightWithOnlyPartialRounds_LeavesCellEmpty()
    {
        var partial = MakeRound(1, 1, 1, 4,
            (1, 4, 0, 0, 0), (2, 0, 4, 0, 0), (3, 0, 0, 4, 0), (4, 0, 0, 0, 4));
        var night = new NightWithRounds(MakeNight(1, "2026-01-15"), new[] { partial });

        var section2 = ExtractPlacements(CsvBuilder.BuildHistoryCsv(new[] { night }, FourPlayers));

        Assert.Equal("Claes;", section2[1]);
        Assert.Equal("Robin;", section2[2]);
        Assert.Equal("Aleksi;", section2[3]);
        Assert.Equal("Jonas;", section2[4]);
    }

    [Fact]
    public void Section2_TiedRound_AssignsSamePlacementToTiedPlayers()
    {
        var round = MakeRound(1, 1, 1, 16,
            (1, 8, 8, 0, 0),    // 56
            (2, 8, 8, 0, 0),    // 56 → delar 1:a med Claes
            (3, 0, 0, 16, 0),   // 32 → 3:a
            (4, 0, 0, 0, 16));  // 16 → 4:a
        var night = new NightWithRounds(MakeNight(1, "2026-01-15"), new[] { round });

        var section2 = ExtractPlacements(CsvBuilder.BuildHistoryCsv(new[] { night }, FourPlayers));

        Assert.Equal("Claes;1", section2[1]);
        Assert.Equal("Robin;1", section2[2]);
        Assert.Equal("Aleksi;3", section2[3]);
        Assert.Equal("Jonas;4", section2[4]);
    }

    [Fact]
    public void Section3_OnlyCompleteRoundsIncrementCounts()
    {
        var complete = MakeRound(1, 1, 1, 16,
            (1, 16, 0, 0, 0), (2, 0, 16, 0, 0), (3, 0, 0, 16, 0), (4, 0, 0, 0, 16));
        var partial = MakeRound(2, 1, 2, 4,
            (1, 4, 0, 0, 0), (2, 0, 4, 0, 0), (3, 0, 0, 4, 0), (4, 0, 0, 0, 4));
        var night = new NightWithRounds(MakeNight(1, "2026-01-15"), new[] { complete, partial });

        var section3 = ExtractTotalscore(CsvBuilder.BuildHistoryCsv(new[] { night }, FourPlayers));

        Assert.Equal("Tot placeringar:;Claes;Robin;Aleksi;Jonas", section3[0]);
        // Endast en komplett omgång → varje spelare har en placering.
        Assert.Equal("1;1;0;0;0", section3[1]);
        Assert.Equal("2;0;1;0;0", section3[2]);
        Assert.Equal("3;0;0;1;0", section3[3]);
        Assert.Equal("4;0;0;0;1", section3[4]);
    }

    [Fact]
    public void Section3_TiedRanking_IncrementsBothPlayersFirstsCounter()
    {
        var tied = MakeRound(1, 1, 1, 16,
            (1, 8, 8, 0, 0),    // 56
            (2, 8, 8, 0, 0),    // 56 → delar 1:a
            (3, 0, 0, 16, 0),   // 32
            (4, 0, 0, 0, 16));  // 16
        var night = new NightWithRounds(MakeNight(1, "2026-01-15"), new[] { tied });

        var section3 = ExtractTotalscore(CsvBuilder.BuildHistoryCsv(new[] { night }, FourPlayers));

        // Claes och Robin delar 1:a → båda får +1 på "1"-raden, ingen får på "2".
        Assert.Equal("1;1;1;0;0", section3[1]);
        Assert.Equal("2;0;0;0;0", section3[2]);
        Assert.Equal("3;0;0;1;0", section3[3]);
        Assert.Equal("4;0;0;0;1", section3[4]);
    }

    [Fact]
    public void NightWithOnlyPartialRounds_StillProducesNightBlockAndSkipsSection2And3()
    {
        // En kväll med bara en partiell omgång (4 banor) ska:
        //   - Skapa ett kvällsblock i sektion 1 med Snitt.
        //   - Lämna kvällens kolumn i sektion 2 tom.
        //   - Inte påverka sektion 3 (totalscore).
        var partial = MakeRound(1, 1, 1, 4,
            (1, 4, 0, 0, 0),    // 16 poäng / 4 banor = 4,00
            (2, 0, 4, 0, 0),    // 12 / 4 = 3,00
            (3, 0, 0, 4, 0),    // 8 / 4 = 2,00
            (4, 0, 0, 0, 4));   // 4 / 4 = 1,00
        var night = new NightWithRounds(MakeNight(1, "2026-01-15"), new[] { partial });

        var csv = CsvBuilder.BuildHistoryCsv(new[] { night }, FourPlayers);
        var lines = csv.Split('\n');

        // Sektion 1 – kvällsblock finns.
        Assert.Equal("Kväll 1;2026-01-15", lines[0]);
        Assert.Equal(";Claes;Robin;Aleksi;Jonas;4", lines[1]);
        Assert.Equal("Snitt;4,00;3,00;2,00;1,00", lines[7]);

        // Sektion 2 – alla celler tomma.
        var section2 = ExtractPlacements(csv);
        Assert.Equal("Claes;", section2[1]);

        // Sektion 3 – inga räknare ökade.
        var section3 = ExtractTotalscore(csv);
        Assert.Equal("1;0;0;0;0", section3[1]);
        Assert.Equal("4;0;0;0;0", section3[4]);
    }

    [Fact]
    public void EmptyNights_ThrowsInvalidOperation()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            CsvBuilder.BuildHistoryCsv(Array.Empty<NightWithRounds>(), FourPlayers));
        Assert.Contains("Inga omgångar", ex.Message);
    }

    [Fact]
    public void FewerThanFourPlayers_ThrowsInvalidOperation()
    {
        var night = new NightWithRounds(MakeNight(1, "2026-01-15"), new[]
        {
            MakeRound(1, 1, 1, 16,
                (1, 16, 0, 0, 0), (2, 0, 16, 0, 0), (3, 0, 0, 16, 0), (4, 0, 0, 0, 16))
        });
        var three = FourPlayers.Take(3).ToList();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            CsvBuilder.BuildHistoryCsv(new[] { night }, three));
        Assert.Contains("4 aktiva spelare", ex.Message);
    }

    private static IReadOnlyList<string> ExtractPlacements(string csv) =>
        ExtractBlock(csv, "Spelare;");

    private static IReadOnlyList<string> ExtractTotalscore(string csv) =>
        ExtractBlock(csv, "Tot placeringar:");

    private static IReadOnlyList<string> ExtractBlock(string csv, string headerPrefix)
    {
        var lines = csv.Split('\n');
        int start = -1;
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].StartsWith(headerPrefix, StringComparison.Ordinal))
            {
                start = i;
                break;
            }
        }
        if (start < 0)
        {
            throw new InvalidOperationException($"Header '{headerPrefix}' hittades inte i CSV.");
        }
        int end = start;
        while (end < lines.Length && lines[end].Length > 0)
        {
            end++;
        }
        return lines[start..end];
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
