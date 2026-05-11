using DoubleDashScore.Data;
using DoubleDashScore.Models;
using DoubleDashScore.Services;
using Xunit;

namespace DoubleDashScore.Tests;

public class CsvBuilderTests
{
    private static readonly IReadOnlyList<Player> FourPlayers = new[]
    {
        new Player { Id = 1, Name = "Anna", DisplayOrder = 0, CreatedAt = DateTime.UtcNow },
        new Player { Id = 2, Name = "Bosse", DisplayOrder = 1, CreatedAt = DateTime.UtcNow },
        new Player { Id = 3, Name = "Cecilia", DisplayOrder = 2, CreatedAt = DateTime.UtcNow },
        new Player { Id = 4, Name = "Dag", DisplayOrder = 3, CreatedAt = DateTime.UtcNow },
    };

    [Fact]
    public void Header_ContainsAllExpectedColumnsInOrder()
    {
        var night = new NightWithRounds(MakeNight(1, "2026-01-15", "Pizzakväll"), new[]
        {
            MakeRound(1, 1, 1, 16,
                (1, 16, 0, 0, 0),
                (2, 0, 16, 0, 0),
                (3, 0, 0, 16, 0),
                (4, 0, 0, 0, 16))
        });

        var csv = CsvBuilder.BuildHistoryCsv(new[] { night }, FourPlayers);
        var header = csv.Split('\n')[0];

        var expected = string.Join(";", new[]
        {
            "Datum", "Kvällsnummer", "Kvällsanteckning", "Omgång", "Banor", "Komplett",
            "Anna 1:or", "Anna 2:or", "Anna 3:or", "Anna 4:or",
            "Bosse 1:or", "Bosse 2:or", "Bosse 3:or", "Bosse 4:or",
            "Cecilia 1:or", "Cecilia 2:or", "Cecilia 3:or", "Cecilia 4:or",
            "Dag 1:or", "Dag 2:or", "Dag 3:or", "Dag 4:or",
            "Anna Poäng", "Anna Placering",
            "Bosse Poäng", "Bosse Placering",
            "Cecilia Poäng", "Cecilia Placering",
            "Dag Poäng", "Dag Placering",
        });
        Assert.Equal(expected, header);
    }

    [Fact]
    public void CompleteRound_FillsPositionCells()
    {
        var night = new NightWithRounds(MakeNight(1, "2026-01-15", note: null), new[]
        {
            MakeRound(1, 1, 1, 16,
                (1, 16, 0, 0, 0),    // 64 → 1st
                (2, 0, 16, 0, 0),    // 48 → 2nd
                (3, 0, 0, 16, 0),    // 32 → 3rd
                (4, 0, 0, 0, 16))    // 16 → 4th
        });

        var rows = CsvBuilder.BuildHistoryCsv(new[] { night }, FourPlayers).Split('\n');
        var data = rows[1].Split(';');

        Assert.Equal("Ja", data[5]);
        // Position cells start at index 6 + 16 = 22 (Anna Poäng), 23 (Anna Placering), ...
        Assert.Equal("64", data[22]);
        Assert.Equal("1", data[23]);
        Assert.Equal("48", data[24]);
        Assert.Equal("2", data[25]);
        Assert.Equal("32", data[26]);
        Assert.Equal("3", data[27]);
        Assert.Equal("16", data[28]);
        Assert.Equal("4", data[29]);
    }

    [Fact]
    public void PartialRound_LeavesPositionCellEmpty_ButFillsPoints()
    {
        var night = new NightWithRounds(MakeNight(1, "2026-01-15", note: null), new[]
        {
            MakeRound(1, 1, 1, 4,
                (1, 4, 0, 0, 0),    // 16
                (2, 0, 4, 0, 0),    // 12
                (3, 0, 0, 4, 0),    // 8
                (4, 0, 0, 0, 4))    // 4
        });

        var rows = CsvBuilder.BuildHistoryCsv(new[] { night }, FourPlayers).Split('\n');
        var data = rows[1].Split(';');

        Assert.Equal("Nej", data[5]);
        Assert.Equal("16", data[22]); // Anna Poäng
        Assert.Equal("", data[23]);   // Anna Placering tom
        Assert.Equal("12", data[24]); // Bosse Poäng
        Assert.Equal("", data[25]);   // Bosse Placering tom
        Assert.Equal("8", data[26]);
        Assert.Equal("", data[27]);
        Assert.Equal("4", data[28]);
        Assert.Equal("", data[29]);
    }

    [Fact]
    public void TiedRound_AssignsSamePositionToTiedPlayers()
    {
        var night = new NightWithRounds(MakeNight(1, "2026-01-15", note: null), new[]
        {
            MakeRound(1, 1, 1, 16,
                (1, 8, 8, 0, 0),    // 56
                (2, 8, 8, 0, 0),    // 56 → delar 1:a med P1
                (3, 0, 0, 16, 0),   // 32 → 3:a
                (4, 0, 0, 0, 16))   // 16 → 4:a
        });

        var rows = CsvBuilder.BuildHistoryCsv(new[] { night }, FourPlayers).Split('\n');
        var data = rows[1].Split(';');

        Assert.Equal("1", data[23]); // Anna placering
        Assert.Equal("1", data[25]); // Bosse placering (delad 1:a)
        Assert.Equal("3", data[27]); // Cecilia 3:a
        Assert.Equal("4", data[29]); // Dag 4:a
    }

    [Fact]
    public void PlayerNameWithSemicolon_GetsQuotedInHeader()
    {
        var playersWithSemi = new[]
        {
            new Player { Id = 1, Name = "Anna; the boss", DisplayOrder = 0, CreatedAt = DateTime.UtcNow },
            new Player { Id = 2, Name = "Bosse", DisplayOrder = 1, CreatedAt = DateTime.UtcNow },
            new Player { Id = 3, Name = "Cecilia", DisplayOrder = 2, CreatedAt = DateTime.UtcNow },
            new Player { Id = 4, Name = "Dag", DisplayOrder = 3, CreatedAt = DateTime.UtcNow },
        };
        var night = new NightWithRounds(MakeNight(1, "2026-01-15", note: null), new[]
        {
            MakeRound(1, 1, 1, 16,
                (1, 16, 0, 0, 0),
                (2, 0, 16, 0, 0),
                (3, 0, 0, 16, 0),
                (4, 0, 0, 0, 16))
        });

        var csv = CsvBuilder.BuildHistoryCsv(new[] { night }, playersWithSemi);

        Assert.Contains("\"Anna; the boss 1:or\"", csv);
        Assert.Contains("\"Anna; the boss Poäng\"", csv);
    }

    [Fact]
    public void NoteWithQuotes_GetsQuotedAndEscaped()
    {
        var night = new NightWithRounds(MakeNight(1, "2026-01-15", "Anna sa \"hej\""), new[]
        {
            MakeRound(1, 1, 1, 16,
                (1, 16, 0, 0, 0),
                (2, 0, 16, 0, 0),
                (3, 0, 0, 16, 0),
                (4, 0, 0, 0, 16))
        });

        var csv = CsvBuilder.BuildHistoryCsv(new[] { night }, FourPlayers);
        var data = csv.Split('\n')[1];

        Assert.Contains("\"Anna sa \"\"hej\"\"\"", data);
    }

    [Fact]
    public void MultipleNightsAndRounds_SortAscendingAndNumberCorrectly()
    {
        var feb = new NightWithRounds(MakeNight(2, "2026-02-15", note: null), new[]
        {
            MakeRound(2, 2, 1, 16,
                (1, 16, 0, 0, 0),
                (2, 0, 16, 0, 0),
                (3, 0, 0, 16, 0),
                (4, 0, 0, 0, 16)),
            MakeRound(3, 2, 2, 16,
                (1, 0, 16, 0, 0),
                (2, 16, 0, 0, 0),
                (3, 0, 0, 16, 0),
                (4, 0, 0, 0, 16))
        });
        var jan = new NightWithRounds(MakeNight(1, "2026-01-15", note: null), new[]
        {
            MakeRound(1, 1, 1, 16,
                (1, 16, 0, 0, 0),
                (2, 0, 16, 0, 0),
                (3, 0, 0, 16, 0),
                (4, 0, 0, 0, 16))
        });

        // Input order: feb, jan. Output: jan as kvällsnummer 1, feb som kvällsnummer 2.
        var rows = CsvBuilder.BuildHistoryCsv(new[] { feb, jan }, FourPlayers).Split('\n');

        // rows[0] = header, [1] jan, [2] feb r1, [3] feb r2
        Assert.Equal("2026-01-15", rows[1].Split(';')[0]);
        Assert.Equal("1", rows[1].Split(';')[1]); // kvällsnummer
        Assert.Equal("2026-02-15", rows[2].Split(';')[0]);
        Assert.Equal("2", rows[2].Split(';')[1]);
        Assert.Equal("1", rows[2].Split(';')[3]); // omgång nummer
        Assert.Equal("2026-02-15", rows[3].Split(';')[0]);
        Assert.Equal("2", rows[3].Split(';')[3]); // omgång 2 av feb-kvällen
    }

    [Fact]
    public void EmptyNights_ThrowsInvalidOperation()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            CsvBuilder.BuildHistoryCsv(Array.Empty<NightWithRounds>(), FourPlayers));
        Assert.Contains("Inga omgångar", ex.Message);
    }

    [Fact]
    public void NightsWithoutRounds_ThrowsInvalidOperation()
    {
        var emptyNight = new NightWithRounds(MakeNight(1, "2026-01-15", note: null), Array.Empty<RoundDetail>());
        Assert.Throws<InvalidOperationException>(() =>
            CsvBuilder.BuildHistoryCsv(new[] { emptyNight }, FourPlayers));
    }

    [Fact]
    public void FewerThanFourPlayers_ThrowsInvalidOperation()
    {
        var night = new NightWithRounds(MakeNight(1, "2026-01-15", note: null), new[]
        {
            MakeRound(1, 1, 1, 16,
                (1, 16, 0, 0, 0),
                (2, 0, 16, 0, 0),
                (3, 0, 0, 16, 0),
                (4, 0, 0, 0, 16))
        });
        var three = FourPlayers.Take(3).ToList();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            CsvBuilder.BuildHistoryCsv(new[] { night }, three));
        Assert.Contains("4 aktiva spelare", ex.Message);
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

    private static GameNight MakeNight(int id, string isoDate, string? note) => new()
    {
        Id = id,
        PlayedOn = DateTime.SpecifyKind(DateTime.Parse(isoDate), DateTimeKind.Utc),
        Note = note,
        CreatedAt = DateTime.UtcNow,
    };
}
