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
    public void EveryRow_Has18Columns()
    {
        var night = OneCompleteNight("2026-01-15");

        var grid = ParseGrid(CsvBuilder.BuildHistoryCsv(new[] { night }, FourPlayers));

        Assert.All(grid, row => Assert.Equal(18, row.Length));
    }

    [Fact]
    public void Section1_HeaderRow_HasNightNumberInColumnAAndDateInColumnB()
    {
        var grid = ParseGrid(CsvBuilder.BuildHistoryCsv(new[] { OneCompleteNight("2026-01-15") }, FourPlayers));

        Assert.Equal("Kväll 1", Cell(grid, 0, 'A'));
        Assert.Equal("2026-01-15", Cell(grid, 0, 'B'));
        Assert.Equal(string.Empty, Cell(grid, 0, 'C'));
        Assert.Equal(string.Empty, Cell(grid, 0, 'D'));
        Assert.Equal(string.Empty, Cell(grid, 0, 'E'));
        Assert.Equal(string.Empty, Cell(grid, 0, 'F'));
    }

    [Fact]
    public void Section1_PlayerNameRow_HasEmptyColumnAThenNamesInBToEAndTotalTracksInF()
    {
        var grid = ParseGrid(CsvBuilder.BuildHistoryCsv(new[] { OneCompleteNight("2026-01-15") }, FourPlayers));

        Assert.Equal(string.Empty, Cell(grid, 1, 'A'));
        Assert.Equal("Claes", Cell(grid, 1, 'B'));
        Assert.Equal("Robin", Cell(grid, 1, 'C'));
        Assert.Equal("Aleksi", Cell(grid, 1, 'D'));
        Assert.Equal("Jonas", Cell(grid, 1, 'E'));
        Assert.Equal("16", Cell(grid, 1, 'F'));
    }

    [Fact]
    public void Section1_TotalTracksInColumnF_SumsAcrossAllRoundsInNight()
    {
        var complete = MakeRound(1, 1, 1, 16,
            (1, 16, 0, 0, 0), (2, 0, 16, 0, 0), (3, 0, 0, 16, 0), (4, 0, 0, 0, 16));
        var partial = MakeRound(2, 1, 2, 4,
            (1, 4, 0, 0, 0), (2, 0, 4, 0, 0), (3, 0, 0, 4, 0), (4, 0, 0, 0, 4));
        var night = new NightWithRounds(MakeNight(1, "2026-01-15"), new[] { complete, partial });

        var grid = ParseGrid(CsvBuilder.BuildHistoryCsv(new[] { night }, FourPlayers));

        Assert.Equal("20", Cell(grid, 1, 'F'));
    }

    [Fact]
    public void Section1_PositionRows_SumCountsAcrossRounds()
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

        var grid = ParseGrid(CsvBuilder.BuildHistoryCsv(new[] { night }, FourPlayers));

        // Row 2 = "1": 7+4=11, 5+0=5, 3+0=3, 1+0=1
        Assert.Equal("1", Cell(grid, 2, 'A'));
        Assert.Equal("11", Cell(grid, 2, 'B'));
        Assert.Equal("5", Cell(grid, 2, 'C'));
        Assert.Equal("3", Cell(grid, 2, 'D'));
        Assert.Equal("1", Cell(grid, 2, 'E'));

        // Row 3 = "2": 5+0=5, 7+4=11, 3+0=3, 1+0=1
        Assert.Equal("2", Cell(grid, 3, 'A'));
        Assert.Equal("5", Cell(grid, 3, 'B'));
        Assert.Equal("11", Cell(grid, 3, 'C'));
        Assert.Equal("3", Cell(grid, 3, 'D'));
        Assert.Equal("1", Cell(grid, 3, 'E'));

        // Row 5 = "4": 1+0=1, 1+0=1, 3+0=3, 11+4=15
        Assert.Equal("4", Cell(grid, 5, 'A'));
        Assert.Equal("1", Cell(grid, 5, 'B'));
        Assert.Equal("1", Cell(grid, 5, 'C'));
        Assert.Equal("3", Cell(grid, 5, 'D'));
        Assert.Equal("15", Cell(grid, 5, 'E'));
    }

    [Fact]
    public void Section1_PoangAndSnitt_ComputedAcrossAllRoundsIncludingPartial()
    {
        // 16 + 4 = 20 banor. Claes: r1 = 7·4+5·3+3·2+1·1 = 50; r2 = 4·4 = 16 → 66 poäng, 66/20 = 3,30.
        var r1 = MakeRound(1, 1, 1, 16,
            (1, 7, 5, 3, 1), (2, 5, 7, 3, 1), (3, 3, 3, 7, 3), (4, 1, 1, 3, 11));
        var r2 = MakeRound(2, 1, 2, 4,
            (1, 4, 0, 0, 0), (2, 0, 4, 0, 0), (3, 0, 0, 4, 0), (4, 0, 0, 0, 4));
        var night = new NightWithRounds(MakeNight(1, "2026-01-15"), new[] { r1, r2 });

        var grid = ParseGrid(CsvBuilder.BuildHistoryCsv(new[] { night }, FourPlayers));

        Assert.Equal("Poäng", Cell(grid, 6, 'A'));
        Assert.Equal("66", Cell(grid, 6, 'B'));

        Assert.Equal("Snitt", Cell(grid, 7, 'A'));
        Assert.Equal("3,30", Cell(grid, 7, 'B')); // sv-SE komma, 2 decimaler
    }

    [Fact]
    public void Section1_TwoNights_BlankRowBetweenAndChronologicalNumbering()
    {
        var jan = OneCompleteNight("2026-01-15");
        var feb = OneCompleteNight("2026-02-15");

        // Skickas i fel ordning — feb först.
        var grid = ParseGrid(CsvBuilder.BuildHistoryCsv(new[] { feb, jan }, FourPlayers));

        // Block 1 (jan) på rader 0–7, blank rad 8, block 2 (feb) på 9–16. Total 17 rader.
        Assert.Equal(17, grid.Length);
        Assert.Equal("Kväll 1", Cell(grid, 0, 'A'));
        Assert.Equal("2026-01-15", Cell(grid, 0, 'B'));
        Assert.Equal("Kväll 2", Cell(grid, 9, 'A'));
        Assert.Equal("2026-02-15", Cell(grid, 9, 'B'));

        // Section 1-kolumner (A–F) på rad 8 är tomma.
        for (char c = 'A'; c <= 'F'; c++)
        {
            Assert.Equal(string.Empty, Cell(grid, 8, c));
        }
    }

    [Fact]
    public void Section2_Header_OnRowZeroInColumnsHThroughL()
    {
        var night = OneCompleteNight("2026-01-15");

        var grid = ParseGrid(CsvBuilder.BuildHistoryCsv(new[] { night }, FourPlayers));

        Assert.Equal("Kväll", Cell(grid, 0, 'H'));
        Assert.Equal("Claes", Cell(grid, 0, 'I'));
        Assert.Equal("Robin", Cell(grid, 0, 'J'));
        Assert.Equal("Aleksi", Cell(grid, 0, 'K'));
        Assert.Equal("Jonas", Cell(grid, 0, 'L'));
    }

    [Fact]
    public void Section2_NightRows_StartAtRowOneWithKvallNumberInH()
    {
        var n1 = OneCompleteNight("2026-01-15");
        var n2 = OneCompleteNight("2026-02-15");

        var grid = ParseGrid(CsvBuilder.BuildHistoryCsv(new[] { n1, n2 }, FourPlayers));

        Assert.Equal("1", Cell(grid, 1, 'H'));
        Assert.Equal("2", Cell(grid, 2, 'H'));
    }

    [Fact]
    public void Section2_MultipleCompleteRoundsInNight_CommaSeparatedNoSpace()
    {
        // Två kompletta omgångar — Claes vinner båda (placering 1,1).
        var r1 = MakeRound(1, 1, 1, 16,
            (1, 16, 0, 0, 0), (2, 0, 16, 0, 0), (3, 0, 0, 16, 0), (4, 0, 0, 0, 16));
        var r2 = MakeRound(2, 1, 2, 16,
            (1, 16, 0, 0, 0), (2, 0, 16, 0, 0), (3, 0, 0, 16, 0), (4, 0, 0, 0, 16));
        var night = new NightWithRounds(MakeNight(1, "2026-01-15"), new[] { r1, r2 });

        var grid = ParseGrid(CsvBuilder.BuildHistoryCsv(new[] { night }, FourPlayers));

        Assert.Equal("1,1", Cell(grid, 1, 'I')); // Claes
        Assert.Equal("2,2", Cell(grid, 1, 'J')); // Robin
        Assert.Equal("3,3", Cell(grid, 1, 'K')); // Aleksi
        Assert.Equal("4,4", Cell(grid, 1, 'L')); // Jonas
    }

    [Fact]
    public void Section2_NightWithOnlyPartialRounds_LeavesPlayerCellsEmpty()
    {
        var partial = MakeRound(1, 1, 1, 4,
            (1, 4, 0, 0, 0), (2, 0, 4, 0, 0), (3, 0, 0, 4, 0), (4, 0, 0, 0, 4));
        var night = new NightWithRounds(MakeNight(1, "2026-01-15"), new[] { partial });

        var grid = ParseGrid(CsvBuilder.BuildHistoryCsv(new[] { night }, FourPlayers));

        // Kvällsnumret skrivs ändå i kolumn H.
        Assert.Equal("1", Cell(grid, 1, 'H'));
        // Spelarcellerna I–L tomma eftersom inga kompletta omgångar fanns.
        for (char c = 'I'; c <= 'L'; c++)
        {
            Assert.Equal(string.Empty, Cell(grid, 1, c));
        }
    }

    [Fact]
    public void Section2_TiedRound_AssignsSamePlacementToTiedPlayers()
    {
        var round = MakeRound(1, 1, 1, 16,
            (1, 8, 8, 0, 0),    // 56
            (2, 8, 8, 0, 0),    // 56 → delar 1:a
            (3, 0, 0, 16, 0),   // 32 → 3:a
            (4, 0, 0, 0, 16));  // 16 → 4:a
        var night = new NightWithRounds(MakeNight(1, "2026-01-15"), new[] { round });

        var grid = ParseGrid(CsvBuilder.BuildHistoryCsv(new[] { night }, FourPlayers));

        Assert.Equal("1", Cell(grid, 1, 'I')); // Claes
        Assert.Equal("1", Cell(grid, 1, 'J')); // Robin (delad 1:a)
        Assert.Equal("3", Cell(grid, 1, 'K')); // Aleksi
        Assert.Equal("4", Cell(grid, 1, 'L')); // Jonas
    }

    [Fact]
    public void Section3_Header_OnRowZeroInColumnsNThroughR()
    {
        var grid = ParseGrid(CsvBuilder.BuildHistoryCsv(new[] { OneCompleteNight("2026-01-15") }, FourPlayers));

        Assert.Equal("Tot placeringar:", Cell(grid, 0, 'N'));
        Assert.Equal("Claes", Cell(grid, 0, 'O'));
        Assert.Equal("Robin", Cell(grid, 0, 'P'));
        Assert.Equal("Aleksi", Cell(grid, 0, 'Q'));
        Assert.Equal("Jonas", Cell(grid, 0, 'R'));
    }

    [Fact]
    public void Section3_OnlyCompleteRoundsIncrementCounts()
    {
        var complete = MakeRound(1, 1, 1, 16,
            (1, 16, 0, 0, 0), (2, 0, 16, 0, 0), (3, 0, 0, 16, 0), (4, 0, 0, 0, 16));
        var partial = MakeRound(2, 1, 2, 4,
            (1, 4, 0, 0, 0), (2, 0, 4, 0, 0), (3, 0, 0, 4, 0), (4, 0, 0, 0, 4));
        var night = new NightWithRounds(MakeNight(1, "2026-01-15"), new[] { complete, partial });

        var grid = ParseGrid(CsvBuilder.BuildHistoryCsv(new[] { night }, FourPlayers));

        // Endast en komplett omgång → en placering per spelare.
        AssertSection3Row(grid, 1, "1", "1", "0", "0", "0");
        AssertSection3Row(grid, 2, "2", "0", "1", "0", "0");
        AssertSection3Row(grid, 3, "3", "0", "0", "1", "0");
        AssertSection3Row(grid, 4, "4", "0", "0", "0", "1");
    }

    [Fact]
    public void Section3_TiedRanking_IncrementsBothPlayersFirstsCounter()
    {
        var tied = MakeRound(1, 1, 1, 16,
            (1, 8, 8, 0, 0), (2, 8, 8, 0, 0), (3, 0, 0, 16, 0), (4, 0, 0, 0, 16));
        var night = new NightWithRounds(MakeNight(1, "2026-01-15"), new[] { tied });

        var grid = ParseGrid(CsvBuilder.BuildHistoryCsv(new[] { night }, FourPlayers));

        // Claes och Robin delar 1:a → båda får +1 på "1"-raden, ingen får på "2".
        AssertSection3Row(grid, 1, "1", "1", "1", "0", "0");
        AssertSection3Row(grid, 2, "2", "0", "0", "0", "0");
        AssertSection3Row(grid, 3, "3", "0", "0", "1", "0");
        AssertSection3Row(grid, 4, "4", "0", "0", "0", "1");
    }

    [Fact]
    public void NightWithOnlyPartialRounds_StillProducesNightBlockAndSkipsSection2And3()
    {
        var partial = MakeRound(1, 1, 1, 4,
            (1, 4, 0, 0, 0),    // 16 poäng / 4 banor = 4,00
            (2, 0, 4, 0, 0),    // 12 / 4 = 3,00
            (3, 0, 0, 4, 0),    // 8 / 4 = 2,00
            (4, 0, 0, 0, 4));   // 4 / 4 = 1,00
        var night = new NightWithRounds(MakeNight(1, "2026-01-15"), new[] { partial });

        var grid = ParseGrid(CsvBuilder.BuildHistoryCsv(new[] { night }, FourPlayers));

        // Sektion 1 – kvällsblock finns med korrekt Snitt.
        Assert.Equal("Kväll 1", Cell(grid, 0, 'A'));
        Assert.Equal("4", Cell(grid, 1, 'F'));
        Assert.Equal("Snitt", Cell(grid, 7, 'A'));
        Assert.Equal("4,00", Cell(grid, 7, 'B'));
        Assert.Equal("3,00", Cell(grid, 7, 'C'));
        Assert.Equal("2,00", Cell(grid, 7, 'D'));
        Assert.Equal("1,00", Cell(grid, 7, 'E'));

        // Sektion 2 – kvällens kolumn (rad 1) har spelarceller tomma.
        for (char c = 'I'; c <= 'L'; c++)
        {
            Assert.Equal(string.Empty, Cell(grid, 1, c));
        }

        // Sektion 3 – inga räknare ökade.
        AssertSection3Row(grid, 1, "1", "0", "0", "0", "0");
        AssertSection3Row(grid, 4, "4", "0", "0", "0", "0");
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
        var night = OneCompleteNight("2026-01-15");
        var three = FourPlayers.Take(3).ToList();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            CsvBuilder.BuildHistoryCsv(new[] { night }, three));
        Assert.Contains("4 aktiva spelare", ex.Message);
    }

    private static void AssertSection3Row(
        string[][] grid, int row,
        string label, string claes, string robin, string aleksi, string jonas)
    {
        Assert.Equal(label, Cell(grid, row, 'N'));
        Assert.Equal(claes, Cell(grid, row, 'O'));
        Assert.Equal(robin, Cell(grid, row, 'P'));
        Assert.Equal(aleksi, Cell(grid, row, 'Q'));
        Assert.Equal(jonas, Cell(grid, row, 'R'));
    }

    private static string Cell(string[][] grid, int row, char column)
    {
        return grid[row][column - 'A'];
    }

    private static string[][] ParseGrid(string csv)
    {
        var lines = csv.Split('\n');
        int count = lines.Length;
        if (count > 0 && lines[count - 1].Length == 0) count--;
        var rows = new string[count][];
        for (int i = 0; i < count; i++)
        {
            rows[i] = lines[i].Split(';');
        }
        return rows;
    }

    private static NightWithRounds OneCompleteNight(string isoDate) => new(
        MakeNight(1, isoDate),
        new[]
        {
            MakeRound(1, 1, 1, 16,
                (1, 16, 0, 0, 0),
                (2, 0, 16, 0, 0),
                (3, 0, 0, 16, 0),
                (4, 0, 0, 0, 16)),
        });

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
