using ClosedXML.Excel;
using DoubleDashScore.Models;
using DoubleDashScore.Services;
using Xunit;

namespace DoubleDashScore.Tests;

public class ExcelImporterTests
{
    private static readonly IReadOnlyList<string> DefaultExcelNames = new[] { "Claes", "Robin", "Aleksi", "Jonas" };

    private static readonly IReadOnlyList<Player> FourPlayers = new[]
    {
        new Player { Id = 1, Name = "Claes", DisplayOrder = 0, CreatedAt = DateTime.UtcNow },
        new Player { Id = 2, Name = "Robin", DisplayOrder = 1, CreatedAt = DateTime.UtcNow },
        new Player { Id = 3, Name = "Aleksi", DisplayOrder = 2, CreatedAt = DateTime.UtcNow },
        new Player { Id = 4, Name = "Jonas", DisplayOrder = 3, CreatedAt = DateTime.UtcNow },
    };

    [Fact]
    public void Parse_OneNightBlock_ReturnsFourAggregatesWithCorrectCounts()
    {
        using var stream = BuildWorkbook(ws =>
        {
            WriteGlobalHeader(ws, DefaultExcelNames);
            WriteNightBlock(ws, blockRow: 2, nightNumber: 1, totalTracks: 16,
                firsts: (16, 0, 0, 0),
                seconds: (0, 16, 0, 0),
                thirds: (0, 0, 16, 0),
                fourths: (0, 0, 0, 16));
            WriteSection3Header(ws);
            WriteSection3Row(ws, 1, (0, 0, 0, 0));
            WriteSection3Row(ws, 2, (0, 0, 0, 0));
            WriteSection3Row(ws, 3, (0, 0, 0, 0));
            WriteSection3Row(ws, 4, (0, 0, 0, 0));
        });

        var result = ExcelImporter.Parse(stream, FourPlayers);

        Assert.Equal(4, result.NightAggregates.Count);
        Assert.Equal(new[] { "Claes", "Robin", "Aleksi", "Jonas" }, result.PlayerNamesInExcelColumnOrder);

        var claes = result.NightAggregates.Single(a => a.PlayerId == 1);
        Assert.Equal(1, claes.NightNumber);
        Assert.Equal(16, claes.FirstPlaces);
        Assert.Equal(0, claes.SecondPlaces);
        Assert.Equal(0, claes.ThirdPlaces);
        Assert.Equal(0, claes.FourthPlaces);
        Assert.Equal(16, claes.TotalTracks);

        var robin = result.NightAggregates.Single(a => a.PlayerId == 2);
        Assert.Equal(16, robin.SecondPlaces);
    }

    [Fact]
    public void Parse_MultipleNightBlocks_IteratesThroughAllAndStopsAtEmpty()
    {
        using var stream = BuildWorkbook(ws =>
        {
            WriteGlobalHeader(ws, DefaultExcelNames);
            WriteNightBlock(ws, blockRow: 2, nightNumber: 1, totalTracks: 16,
                firsts: (16, 0, 0, 0),
                seconds: (0, 16, 0, 0),
                thirds: (0, 0, 16, 0),
                fourths: (0, 0, 0, 16));
            // Stride 8 → andra blocket på rad 2 + 8 = 10.
            WriteNightBlock(ws, blockRow: 10, nightNumber: 2, totalTracks: 16,
                firsts: (0, 16, 0, 0),
                seconds: (16, 0, 0, 0),
                thirds: (0, 0, 16, 0),
                fourths: (0, 0, 0, 16));
            WriteSection3Header(ws);
            WriteSection3Row(ws, 1, (0, 0, 0, 0));
            WriteSection3Row(ws, 2, (0, 0, 0, 0));
            WriteSection3Row(ws, 3, (0, 0, 0, 0));
            WriteSection3Row(ws, 4, (0, 0, 0, 0));
        });

        var result = ExcelImporter.Parse(stream, FourPlayers);

        Assert.Equal(8, result.NightAggregates.Count);
        Assert.Equal(new[] { 1, 2 }, result.NightAggregates.Select(a => a.NightNumber).Distinct().OrderBy(n => n));
    }

    [Fact]
    public void Parse_Section2_IntegerCell_GivesSinglePlacement()
    {
        using var stream = BuildWorkbook(ws =>
        {
            WriteGlobalHeader(ws, DefaultExcelNames);
            WriteNightBlock(ws, blockRow: 2, nightNumber: 35, totalTracks: 16,
                firsts: (16, 0, 0, 0),
                seconds: (0, 16, 0, 0),
                thirds: (0, 0, 16, 0),
                fourths: (0, 0, 0, 16));
            ws.Cell(55, "V").Value = 1;
            ws.Cell(55, "W").Value = 2;
            ws.Cell(55, "X").Value = 3;
            ws.Cell(55, "Y").Value = 4;
            WriteSection3Header(ws);
            WriteSection3Row(ws, 1, (0, 0, 0, 0));
            WriteSection3Row(ws, 2, (0, 0, 0, 0));
            WriteSection3Row(ws, 3, (0, 0, 0, 0));
            WriteSection3Row(ws, 4, (0, 0, 0, 0));
        });

        var result = ExcelImporter.Parse(stream, FourPlayers);

        Assert.Equal(4, result.RoundPlacements.Count);
        var claes = result.RoundPlacements.Single(p => p.PlayerId == 1);
        Assert.Equal(35, claes.NightNumber);
        Assert.Equal(1, claes.RoundIndex);
        Assert.Equal(1, claes.Position);
    }

    [Fact]
    public void Parse_Section2_FloatCell_GivesTwoPlacements()
    {
        using var stream = BuildWorkbook(ws =>
        {
            WriteGlobalHeader(ws, DefaultExcelNames);
            // Två kompletta omgångar. Placeringar:
            //   Claes 1,3   Robin 2,4   Aleksi 3,2   Jonas 4,1
            // Räknare = 16 per omgång × 2 omgångar = 32 per spelare.
            WriteNightBlock(ws, blockRow: 2, nightNumber: 35, totalTracks: 32,
                firsts:  (16,  0,  0, 16),
                seconds: ( 0, 16, 16,  0),
                thirds:  (16,  0, 16,  0),
                fourths: ( 0, 16,  0, 16));
            ws.Cell(55, "V").Value = 1.3;
            ws.Cell(55, "W").Value = 2.4;
            ws.Cell(55, "X").Value = 3.2;
            ws.Cell(55, "Y").Value = 4.1;
            WriteSection3Header(ws);
            WriteSection3Row(ws, 1, (0, 0, 0, 0));
            WriteSection3Row(ws, 2, (0, 0, 0, 0));
            WriteSection3Row(ws, 3, (0, 0, 0, 0));
            WriteSection3Row(ws, 4, (0, 0, 0, 0));
        });

        var result = ExcelImporter.Parse(stream, FourPlayers);

        var claesPlacements = result.RoundPlacements
            .Where(p => p.PlayerId == 1)
            .OrderBy(p => p.RoundIndex)
            .Select(p => p.Position)
            .ToList();
        Assert.Equal(new[] { 1, 3 }, claesPlacements);

        var jonasPlacements = result.RoundPlacements
            .Where(p => p.PlayerId == 4)
            .OrderBy(p => p.RoundIndex)
            .Select(p => p.Position)
            .ToList();
        Assert.Equal(new[] { 4, 1 }, jonasPlacements);
    }

    [Fact]
    public void Parse_Section2_TiedRanking_PutsSamePlacementOnBothPlayers()
    {
        using var stream = BuildWorkbook(ws =>
        {
            WriteGlobalHeader(ws, DefaultExcelNames);
            WriteNightBlock(ws, blockRow: 2, nightNumber: 35, totalTracks: 16,
                firsts: (8, 8, 0, 0),
                seconds: (8, 8, 0, 0),
                thirds: (0, 0, 16, 0),
                fourths: (0, 0, 0, 16));
            ws.Cell(55, "V").Value = 1;
            ws.Cell(55, "W").Value = 1;
            ws.Cell(55, "X").Value = 3;
            ws.Cell(55, "Y").Value = 4;
            WriteSection3Header(ws);
            WriteSection3Row(ws, 1, (0, 0, 0, 0));
            WriteSection3Row(ws, 2, (0, 0, 0, 0));
            WriteSection3Row(ws, 3, (0, 0, 0, 0));
            WriteSection3Row(ws, 4, (0, 0, 0, 0));
        });

        var result = ExcelImporter.Parse(stream, FourPlayers);

        Assert.Equal(1, result.RoundPlacements.Single(p => p.PlayerId == 1).Position);
        Assert.Equal(1, result.RoundPlacements.Single(p => p.PlayerId == 2).Position);
        Assert.Equal(3, result.RoundPlacements.Single(p => p.PlayerId == 3).Position);
        Assert.Equal(4, result.RoundPlacements.Single(p => p.PlayerId == 4).Position);
    }

    [Fact]
    public void Parse_Section2_FloatWithOutOfRangePosition_Throws()
    {
        using var stream = BuildWorkbook(ws =>
        {
            WriteGlobalHeader(ws, DefaultExcelNames);
            WriteNightBlock(ws, blockRow: 2, nightNumber: 35, totalTracks: 16,
                firsts: (16, 0, 0, 0),
                seconds: (0, 16, 0, 0),
                thirds: (0, 0, 16, 0),
                fourths: (0, 0, 0, 16));
            ws.Cell(55, "V").Value = 1.5;
            WriteSection3Header(ws);
            WriteSection3Row(ws, 1, (0, 0, 0, 0));
            WriteSection3Row(ws, 2, (0, 0, 0, 0));
            WriteSection3Row(ws, 3, (0, 0, 0, 0));
            WriteSection3Row(ws, 4, (0, 0, 0, 0));
        });

        var ex = Assert.Throws<InvalidDataException>(() => ExcelImporter.Parse(stream, FourPlayers));
        Assert.Contains("utanför 1-4", ex.Message);
    }

    [Fact]
    public void Parse_Section2_MixedEmptyCells_Throws()
    {
        using var stream = BuildWorkbook(ws =>
        {
            WriteGlobalHeader(ws, DefaultExcelNames);
            WriteNightBlock(ws, blockRow: 2, nightNumber: 35, totalTracks: 16,
                firsts: (16, 0, 0, 0),
                seconds: (0, 16, 0, 0),
                thirds: (0, 0, 16, 0),
                fourths: (0, 0, 0, 16));
            ws.Cell(55, "V").Value = 1;
            ws.Cell(55, "W").Value = 2;
            // X och Y lämnas tomma.
            WriteSection3Header(ws);
            WriteSection3Row(ws, 1, (0, 0, 0, 0));
            WriteSection3Row(ws, 2, (0, 0, 0, 0));
            WriteSection3Row(ws, 3, (0, 0, 0, 0));
            WriteSection3Row(ws, 4, (0, 0, 0, 0));
        });

        var ex = Assert.Throws<InvalidDataException>(() => ExcelImporter.Parse(stream, FourPlayers));
        Assert.Contains("olika antal placeringar", ex.Message);
    }

    [Fact]
    public void Parse_NightBlock_CountsDoNotSumToTotalTracks_Throws()
    {
        using var stream = BuildWorkbook(ws =>
        {
            WriteGlobalHeader(ws, DefaultExcelNames);
            WriteNightBlock(ws, blockRow: 2, nightNumber: 1, totalTracks: 16,
                firsts: (15, 0, 0, 0),
                seconds: (0, 16, 0, 0),
                thirds: (0, 0, 16, 0),
                fourths: (0, 0, 0, 16));
            WriteSection3Header(ws);
            WriteSection3Row(ws, 1, (0, 0, 0, 0));
            WriteSection3Row(ws, 2, (0, 0, 0, 0));
            WriteSection3Row(ws, 3, (0, 0, 0, 0));
            WriteSection3Row(ws, 4, (0, 0, 0, 0));
        });

        var ex = Assert.Throws<InvalidDataException>(() => ExcelImporter.Parse(stream, FourPlayers));
        Assert.Contains("15 banor", ex.Message);
    }

    [Fact]
    public void Parse_PlayerNameNotInApp_ThrowsWithFriendlyMessage()
    {
        using var stream = BuildWorkbook(ws =>
        {
            WriteGlobalHeader(ws, new[] { "Claes", "Robin", "Aleksi", "Pelle" });
            WriteNightBlock(ws, blockRow: 2, nightNumber: 1, totalTracks: 16,
                firsts: (16, 0, 0, 0),
                seconds: (0, 16, 0, 0),
                thirds: (0, 0, 16, 0),
                fourths: (0, 0, 0, 16));
            WriteSection3HeaderWithNames(ws, new[] { "Claes", "Robin", "Aleksi", "Pelle" });
            WriteSection3Row(ws, 1, (0, 0, 0, 0));
            WriteSection3Row(ws, 2, (0, 0, 0, 0));
            WriteSection3Row(ws, 3, (0, 0, 0, 0));
            WriteSection3Row(ws, 4, (0, 0, 0, 0));
        });

        var ex = Assert.Throws<InvalidDataException>(() => ExcelImporter.Parse(stream, FourPlayers));
        Assert.Contains("Pelle", ex.Message);
        Assert.Contains("Döp om", ex.Message);
    }

    [Fact]
    public void Parse_NameMatchingIsCaseInsensitive()
    {
        using var stream = BuildWorkbook(ws =>
        {
            WriteGlobalHeader(ws, new[] { "claes", "ROBIN", "Aleksi", "jonas" });
            WriteNightBlock(ws, blockRow: 2, nightNumber: 1, totalTracks: 16,
                firsts: (16, 0, 0, 0),
                seconds: (0, 16, 0, 0),
                thirds: (0, 0, 16, 0),
                fourths: (0, 0, 0, 16));
            WriteSection3HeaderWithNames(ws, new[] { "claes", "ROBIN", "Aleksi", "jonas" });
            WriteSection3Row(ws, 1, (0, 0, 0, 0));
            WriteSection3Row(ws, 2, (0, 0, 0, 0));
            WriteSection3Row(ws, 3, (0, 0, 0, 0));
            WriteSection3Row(ws, 4, (0, 0, 0, 0));
        });

        var result = ExcelImporter.Parse(stream, FourPlayers);

        Assert.Equal(new[] { "claes", "ROBIN", "Aleksi", "jonas" }, result.PlayerNamesInExcelColumnOrder);
        Assert.Equal(16, result.NightAggregates.Single(a => a.PlayerId == 1).FirstPlaces);
    }

    [Fact]
    public void Parse_ExcelColumnOrderDifferentFromAppDisplayOrder_MapsByName()
    {
        // Excel-ordningen: Robin, Claes, Aleksi, Jonas (kolumn B-E i globala headern).
        using var stream = BuildWorkbook(ws =>
        {
            WriteGlobalHeader(ws, new[] { "Robin", "Claes", "Aleksi", "Jonas" });
            WriteNightBlock(ws, blockRow: 2, nightNumber: 1, totalTracks: 16,
                firsts: (16, 0, 0, 0),    // Robin vann alla
                seconds: (0, 16, 0, 0),   // Claes 2:a
                thirds: (0, 0, 16, 0),
                fourths: (0, 0, 0, 16));
            WriteSection3HeaderWithNames(ws, new[] { "Robin", "Claes", "Aleksi", "Jonas" });
            WriteSection3Row(ws, 1, (0, 0, 0, 0));
            WriteSection3Row(ws, 2, (0, 0, 0, 0));
            WriteSection3Row(ws, 3, (0, 0, 0, 0));
            WriteSection3Row(ws, 4, (0, 0, 0, 0));
        });

        var result = ExcelImporter.Parse(stream, FourPlayers);

        // Claes (Id=1) ska få 2:a-platsen från kolumn C.
        Assert.Equal(0, result.NightAggregates.Single(a => a.PlayerId == 1).FirstPlaces);
        Assert.Equal(16, result.NightAggregates.Single(a => a.PlayerId == 1).SecondPlaces);
        // Robin (Id=2) ska få 1:a-platserna från kolumn B.
        Assert.Equal(16, result.NightAggregates.Single(a => a.PlayerId == 2).FirstPlaces);
    }

    [Fact]
    public void Parse_EmptyWorkbook_Throws()
    {
        using var stream = BuildWorkbook(_ => { /* tom */ });

        var ex = Assert.Throws<InvalidDataException>(() => ExcelImporter.Parse(stream, FourPlayers));
        Assert.Contains("Spelarnamn saknas", ex.Message);
    }

    [Fact]
    public void Parse_Section3_ReadsSnapshotCounts()
    {
        using var stream = BuildWorkbook(ws =>
        {
            WriteGlobalHeader(ws, DefaultExcelNames);
            WriteNightBlock(ws, blockRow: 2, nightNumber: 1, totalTracks: 16,
                firsts: (16, 0, 0, 0),
                seconds: (0, 16, 0, 0),
                thirds: (0, 0, 16, 0),
                fourths: (0, 0, 0, 16));
            WriteSection3Header(ws);
            WriteSection3Row(ws, 1, (58, 0, 25, 5));
            WriteSection3Row(ws, 2, (12, 7, 43, 17));
            WriteSection3Row(ws, 3, (10, 33, 10, 29));
            WriteSection3Row(ws, 4, (2, 42, 4, 28));
        });

        var result = ExcelImporter.Parse(stream, FourPlayers);

        Assert.Equal(4, result.PositionTotalsSnapshot.Count);
        var claes = result.PositionTotalsSnapshot.Single(s => s.PlayerId == 1);
        Assert.Equal(58, claes.Firsts);
        Assert.Equal(12, claes.Seconds);
        Assert.Equal(10, claes.Thirds);
        Assert.Equal(2, claes.Fourths);

        var robin = result.PositionTotalsSnapshot.Single(s => s.PlayerId == 2);
        Assert.Equal(0, robin.Firsts);
        Assert.Equal(42, robin.Fourths);
    }

    [Fact]
    public void Parse_FewerThanFourActivePlayers_Throws()
    {
        using var stream = BuildWorkbook(ws =>
        {
            WriteGlobalHeader(ws, DefaultExcelNames);
            WriteNightBlock(ws, blockRow: 2, nightNumber: 1, totalTracks: 16,
                firsts: (16, 0, 0, 0),
                seconds: (0, 16, 0, 0),
                thirds: (0, 0, 16, 0),
                fourths: (0, 0, 0, 16));
            WriteSection3Header(ws);
            WriteSection3Row(ws, 1, (0, 0, 0, 0));
            WriteSection3Row(ws, 2, (0, 0, 0, 0));
            WriteSection3Row(ws, 3, (0, 0, 0, 0));
            WriteSection3Row(ws, 4, (0, 0, 0, 0));
        });

        var three = FourPlayers.Take(3).ToList();

        var ex = Assert.Throws<InvalidOperationException>(() => ExcelImporter.Parse(stream, three));
        Assert.Contains("4 aktiva spelare", ex.Message);
    }

    private static MemoryStream BuildWorkbook(Action<IXLWorksheet> setup)
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.AddWorksheet("Sheet1");
        setup(ws);
        var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        return stream;
    }

    private static void WriteGlobalHeader(IXLWorksheet ws, IReadOnlyList<string> playerNames)
    {
        ws.Cell(1, "A").Value = "Position";
        ws.Cell(1, "B").Value = playerNames[0];
        ws.Cell(1, "C").Value = playerNames[1];
        ws.Cell(1, "D").Value = playerNames[2];
        ws.Cell(1, "E").Value = playerNames[3];
        ws.Cell(1, "F").Value = "Antal banor:";
    }

    private static void WriteNightBlock(
        IXLWorksheet ws, int blockRow, int nightNumber, int totalTracks,
        (int, int, int, int) firsts,
        (int, int, int, int) seconds,
        (int, int, int, int) thirds,
        (int, int, int, int) fourths)
    {
        ws.Cell(blockRow, "A").Value = $"Kväll {nightNumber}";
        ws.Cell(blockRow, "F").Value = totalTracks;

        WriteCountRow(ws, blockRow + 1, "1", firsts);
        WriteCountRow(ws, blockRow + 2, "2", seconds);
        WriteCountRow(ws, blockRow + 3, "3", thirds);
        WriteCountRow(ws, blockRow + 4, "4", fourths);

        var (f1, f2, f3, f4) = firsts;
        var (s1, s2, s3, s4) = seconds;
        var (t1, t2, t3, t4) = thirds;
        var (fo1, fo2, fo3, fo4) = fourths;
        ws.Cell(blockRow + 5, "A").Value = "Poäng";
        ws.Cell(blockRow + 5, "B").Value = 4 * f1 + 3 * s1 + 2 * t1 + fo1;
        ws.Cell(blockRow + 5, "C").Value = 4 * f2 + 3 * s2 + 2 * t2 + fo2;
        ws.Cell(blockRow + 5, "D").Value = 4 * f3 + 3 * s3 + 2 * t3 + fo3;
        ws.Cell(blockRow + 5, "E").Value = 4 * f4 + 3 * s4 + 2 * t4 + fo4;

        ws.Cell(blockRow + 6, "A").Value = "Snitt";
    }

    private static void WriteCountRow(IXLWorksheet ws, int row, string label, (int, int, int, int) counts)
    {
        var (a, b, c, d) = counts;
        ws.Cell(row, "A").Value = label;
        ws.Cell(row, "B").Value = a;
        ws.Cell(row, "C").Value = b;
        ws.Cell(row, "D").Value = c;
        ws.Cell(row, "E").Value = d;
    }

    private static void WriteSection3Header(IXLWorksheet ws)
    {
        WriteSection3HeaderWithNames(ws, DefaultExcelNames);
    }

    private static void WriteSection3HeaderWithNames(IXLWorksheet ws, IReadOnlyList<string> names)
    {
        ws.Cell(55, "AA").Value = "Tot placeringar:";
        ws.Cell(55, "AB").Value = names[0];
        ws.Cell(55, "AC").Value = names[1];
        ws.Cell(55, "AD").Value = names[2];
        ws.Cell(55, "AE").Value = names[3];
    }

    private static void WriteSection3Row(IXLWorksheet ws, int position, (int, int, int, int) counts)
    {
        var row = 55 + position;
        var (a, b, c, d) = counts;
        ws.Cell(row, "AA").Value = position;
        ws.Cell(row, "AB").Value = a;
        ws.Cell(row, "AC").Value = b;
        ws.Cell(row, "AD").Value = c;
        ws.Cell(row, "AE").Value = d;
    }
}
