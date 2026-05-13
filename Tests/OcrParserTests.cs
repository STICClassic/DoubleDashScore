using DoubleDashScore.Models;
using DoubleDashScore.Services;
using Xunit;

namespace DoubleDashScore.Tests;

public class OcrParserTests
{
    private const int W = 1000;
    private const int H = 800;

    private static OcrToken Tok(string text, int cx, int cy, int halfW = 20, int halfH = 15) =>
        new(text, new OcrBoundingBox(cx - halfW, cy - halfH, cx + halfW, cy + halfH));

    private static OcrResult Result(params OcrToken[] tokens) =>
        new(tokens, W, H);

    [Fact]
    public void Parse_PerfectFourByFour_ReturnsExactCounters()
    {
        var c1 = W / 8;
        var c2 = 3 * W / 8;
        var c3 = 5 * W / 8;
        var c4 = 7 * W / 8;
        var rowYs = new[] { 100, 200, 300, 400 };

        var tokens = new[]
        {
            Tok("6", c1, rowYs[0]), Tok("4", c1, rowYs[1]), Tok("4", c1, rowYs[2]), Tok("2", c1, rowYs[3]),
            Tok("2", c2, rowYs[0]), Tok("5", c2, rowYs[1]), Tok("3", c2, rowYs[2]), Tok("6", c2, rowYs[3]),
            Tok("5", c3, rowYs[0]), Tok("5", c3, rowYs[1]), Tok("3", c3, rowYs[2]), Tok("3", c3, rowYs[3]),
            Tok("3", c4, rowYs[0]), Tok("2", c4, rowYs[1]), Tok("6", c4, rowYs[2]), Tok("5", c4, rowYs[3]),
        };

        var parsed = OcrParser.Parse(Result(tokens));

        Assert.Equal(16, parsed.InferredTrackCount);
        Assert.Empty(parsed.Warnings);
        Assert.Equal(new PlayerSlotCounters(0, 6, 4, 4, 2), parsed.Slots[0]);
        Assert.Equal(new PlayerSlotCounters(1, 2, 5, 3, 6), parsed.Slots[1]);
        Assert.Equal(new PlayerSlotCounters(2, 5, 5, 3, 3), parsed.Slots[2]);
        Assert.Equal(new PlayerSlotCounters(3, 3, 2, 6, 5), parsed.Slots[3]);
    }

    [Fact]
    public void Parse_MultiDigitTokens_ParseCorrectly()
    {
        var c1 = W / 8;
        var tokens = new[]
        {
            Tok("16", c1, 100), Tok("0", c1, 200), Tok("0", c1, 300), Tok("0", c1, 400),
            Tok("0", 3 * W / 8, 100), Tok("16", 3 * W / 8, 200), Tok("0", 3 * W / 8, 300), Tok("0", 3 * W / 8, 400),
            Tok("0", 5 * W / 8, 100), Tok("0", 5 * W / 8, 200), Tok("16", 5 * W / 8, 300), Tok("0", 5 * W / 8, 400),
            Tok("0", 7 * W / 8, 100), Tok("0", 7 * W / 8, 200), Tok("0", 7 * W / 8, 300), Tok("16", 7 * W / 8, 400),
        };

        var parsed = OcrParser.Parse(Result(tokens));

        Assert.Equal(16, parsed.InferredTrackCount);
        Assert.Equal(16, parsed.Slots[0].FirstPlaces);
        Assert.Equal(16, parsed.Slots[1].SecondPlaces);
        Assert.Equal(16, parsed.Slots[2].ThirdPlaces);
        Assert.Equal(16, parsed.Slots[3].FourthPlaces);
        Assert.Empty(parsed.Warnings);
    }

    [Fact]
    public void Parse_EmptyTokens_ReturnsAllZerosWithWarning()
    {
        var parsed = OcrParser.Parse(new OcrResult(Array.Empty<OcrToken>(), W, H));

        Assert.Equal(0, parsed.InferredTrackCount);
        Assert.Equal(4, parsed.Slots.Count);
        Assert.All(parsed.Slots, s => Assert.Equal(0, s.Sum));
        Assert.Contains(parsed.Warnings, w => w.Contains("hittade inga siffror"));
    }

    [Fact]
    public void Parse_ColumnWithThreeTokens_FillsRemainderWithZeroAndWarns()
    {
        var c1 = W / 8;
        var tokens = new[]
        {
            Tok("4", c1, 100), Tok("4", c1, 200), Tok("4", c1, 300),
            Tok("3", 3 * W / 8, 100), Tok("3", 3 * W / 8, 200), Tok("3", 3 * W / 8, 300), Tok("3", 3 * W / 8, 400),
            Tok("3", 5 * W / 8, 100), Tok("3", 5 * W / 8, 200), Tok("3", 5 * W / 8, 300), Tok("3", 5 * W / 8, 400),
            Tok("3", 7 * W / 8, 100), Tok("3", 7 * W / 8, 200), Tok("3", 7 * W / 8, 300), Tok("3", 7 * W / 8, 400),
        };

        var parsed = OcrParser.Parse(Result(tokens));

        Assert.Equal(new PlayerSlotCounters(0, 4, 4, 4, 0), parsed.Slots[0]);
        Assert.Contains(parsed.Warnings, w => w.Contains("Kolumn P1") && w.Contains("3"));
    }

    [Fact]
    public void Parse_ColumnWithFiveTokens_DropsLastByYAndWarns()
    {
        var c1 = W / 8;
        var tokens = new[]
        {
            Tok("1", c1, 100), Tok("2", c1, 200), Tok("3", c1, 300), Tok("4", c1, 400), Tok("9", c1, 500),
            Tok("4", 3 * W / 8, 100), Tok("4", 3 * W / 8, 200), Tok("4", 3 * W / 8, 300), Tok("4", 3 * W / 8, 400),
            Tok("4", 5 * W / 8, 100), Tok("4", 5 * W / 8, 200), Tok("4", 5 * W / 8, 300), Tok("4", 5 * W / 8, 400),
            Tok("4", 7 * W / 8, 100), Tok("4", 7 * W / 8, 200), Tok("4", 7 * W / 8, 300), Tok("4", 7 * W / 8, 400),
        };

        var parsed = OcrParser.Parse(Result(tokens));

        Assert.Equal(new PlayerSlotCounters(0, 1, 2, 3, 4), parsed.Slots[0]);
        Assert.Contains(parsed.Warnings, w => w.Contains("Kolumn P1") && w.Contains("5"));
    }

    [Fact]
    public void Parse_TokenValueOutOfRange_FilteredOutWithWarning()
    {
        var c1 = W / 8;
        var tokens = new[]
        {
            Tok("99", c1, 50),
            Tok("4", c1, 100), Tok("4", c1, 200), Tok("4", c1, 300), Tok("4", c1, 400),
            Tok("4", 3 * W / 8, 100), Tok("4", 3 * W / 8, 200), Tok("4", 3 * W / 8, 300), Tok("4", 3 * W / 8, 400),
            Tok("4", 5 * W / 8, 100), Tok("4", 5 * W / 8, 200), Tok("4", 5 * W / 8, 300), Tok("4", 5 * W / 8, 400),
            Tok("4", 7 * W / 8, 100), Tok("4", 7 * W / 8, 200), Tok("4", 7 * W / 8, 300), Tok("4", 7 * W / 8, 400),
        };

        var parsed = OcrParser.Parse(Result(tokens));

        Assert.Equal(16, parsed.InferredTrackCount);
        Assert.Equal(new PlayerSlotCounters(0, 4, 4, 4, 4), parsed.Slots[0]);
        Assert.Contains(parsed.Warnings, w => w.Contains("ignorerades"));
    }

    [Fact]
    public void Parse_NonNumericTokens_AreIgnoredSilently()
    {
        var c1 = W / 8;
        var tokens = new[]
        {
            new OcrToken("Claes", new OcrBoundingBox(0, 0, 100, 30)),
            new OcrToken("P1", new OcrBoundingBox(0, 40, 100, 70)),
            new OcrToken("1st", new OcrBoundingBox(0, 80, 50, 110)),
            new OcrToken("-", new OcrBoundingBox(60, 80, 70, 110)),
            Tok("4", c1, 100), Tok("4", c1, 200), Tok("4", c1, 300), Tok("4", c1, 400),
            Tok("4", 3 * W / 8, 100), Tok("4", 3 * W / 8, 200), Tok("4", 3 * W / 8, 300), Tok("4", 3 * W / 8, 400),
            Tok("4", 5 * W / 8, 100), Tok("4", 5 * W / 8, 200), Tok("4", 5 * W / 8, 300), Tok("4", 5 * W / 8, 400),
            Tok("4", 7 * W / 8, 100), Tok("4", 7 * W / 8, 200), Tok("4", 7 * W / 8, 300), Tok("4", 7 * W / 8, 400),
        };

        var parsed = OcrParser.Parse(Result(tokens));

        Assert.Equal(16, parsed.InferredTrackCount);
        Assert.Empty(parsed.Warnings);
    }

    [Fact]
    public void Parse_RowLabel_1st_StaysAsSingleToken_IsFiltered()
    {
        var c1 = W / 8;
        var tokens = new[]
        {
            new OcrToken("1st", new OcrBoundingBox(c1 - 60, 90, c1 - 20, 110)),
            new OcrToken("2nd", new OcrBoundingBox(c1 - 60, 190, c1 - 20, 210)),
            new OcrToken("3rd", new OcrBoundingBox(c1 - 60, 290, c1 - 20, 310)),
            new OcrToken("4th", new OcrBoundingBox(c1 - 60, 390, c1 - 20, 410)),
            Tok("4", c1, 100), Tok("4", c1, 200), Tok("4", c1, 300), Tok("4", c1, 400),
            Tok("4", 3 * W / 8, 100), Tok("4", 3 * W / 8, 200), Tok("4", 3 * W / 8, 300), Tok("4", 3 * W / 8, 400),
            Tok("4", 5 * W / 8, 100), Tok("4", 5 * W / 8, 200), Tok("4", 5 * W / 8, 300), Tok("4", 5 * W / 8, 400),
            Tok("4", 7 * W / 8, 100), Tok("4", 7 * W / 8, 200), Tok("4", 7 * W / 8, 300), Tok("4", 7 * W / 8, 400),
        };

        var parsed = OcrParser.Parse(Result(tokens));

        Assert.Equal(16, parsed.InferredTrackCount);
        Assert.Empty(parsed.Warnings);
    }

    [Fact]
    public void Parse_RowLabelSegmentedAsBareDigit_PollutesColumn_Documented()
    {
        var c1 = W / 8;
        var tokens = new[]
        {
            Tok("1", c1 - 60, 100), Tok("2", c1 - 60, 200), Tok("3", c1 - 60, 300), Tok("4", c1 - 60, 400),
            Tok("4", c1, 100), Tok("4", c1, 200), Tok("4", c1, 300), Tok("4", c1, 400),
            Tok("4", 3 * W / 8, 100), Tok("4", 3 * W / 8, 200), Tok("4", 3 * W / 8, 300), Tok("4", 3 * W / 8, 400),
            Tok("4", 5 * W / 8, 100), Tok("4", 5 * W / 8, 200), Tok("4", 5 * W / 8, 300), Tok("4", 5 * W / 8, 400),
            Tok("4", 7 * W / 8, 100), Tok("4", 7 * W / 8, 200), Tok("4", 7 * W / 8, 300), Tok("4", 7 * W / 8, 400),
        };

        var parsed = OcrParser.Parse(Result(tokens));

        Assert.NotEmpty(parsed.Warnings);
        Assert.Contains(parsed.Warnings, w => w.Contains("Kolumn P1"));
    }

    [Fact]
    public void Parse_QuartileBoundary_TokenAtQuarterFallsInSecondColumn()
    {
        var boundary = W / 4;
        var tokens = new[]
        {
            Tok("1", boundary, 100), Tok("1", boundary, 200), Tok("1", boundary, 300), Tok("1", boundary, 400),
            Tok("0", 5 * W / 8, 100), Tok("0", 5 * W / 8, 200), Tok("0", 5 * W / 8, 300), Tok("0", 5 * W / 8, 400),
            Tok("0", 7 * W / 8, 100), Tok("0", 7 * W / 8, 200), Tok("0", 7 * W / 8, 300), Tok("0", 7 * W / 8, 400),
        };

        var parsed = OcrParser.Parse(Result(tokens));

        Assert.Equal(0, parsed.Slots[0].Sum);
        Assert.Equal(4, parsed.Slots[1].Sum);
    }

    [Fact]
    public void Parse_InvertedYOrder_SortsToAscendingY()
    {
        var c1 = W / 8;
        var tokens = new[]
        {
            Tok("4", c1, 400), Tok("3", c1, 300), Tok("2", c1, 200), Tok("1", c1, 100),
            Tok("0", 3 * W / 8, 100), Tok("0", 3 * W / 8, 200), Tok("0", 3 * W / 8, 300), Tok("10", 3 * W / 8, 400),
            Tok("0", 5 * W / 8, 100), Tok("0", 5 * W / 8, 200), Tok("0", 5 * W / 8, 300), Tok("10", 5 * W / 8, 400),
            Tok("0", 7 * W / 8, 100), Tok("0", 7 * W / 8, 200), Tok("0", 7 * W / 8, 300), Tok("10", 7 * W / 8, 400),
        };

        var parsed = OcrParser.Parse(Result(tokens));

        Assert.Equal(new PlayerSlotCounters(0, 1, 2, 3, 4), parsed.Slots[0]);
    }

    [Fact]
    public void Parse_AsymmetricColumnSums_WarnsButReturnsValues()
    {
        var c1 = W / 8;
        var tokens = new[]
        {
            Tok("4", c1, 100), Tok("4", c1, 200), Tok("4", c1, 300), Tok("4", c1, 400),
            Tok("4", 3 * W / 8, 100), Tok("4", 3 * W / 8, 200), Tok("4", 3 * W / 8, 300), Tok("3", 3 * W / 8, 400),
            Tok("4", 5 * W / 8, 100), Tok("4", 5 * W / 8, 200), Tok("4", 5 * W / 8, 300), Tok("4", 5 * W / 8, 400),
            Tok("4", 7 * W / 8, 100), Tok("4", 7 * W / 8, 200), Tok("4", 7 * W / 8, 300), Tok("4", 7 * W / 8, 400),
        };

        var parsed = OcrParser.Parse(Result(tokens));

        Assert.Equal(16, parsed.InferredTrackCount);
        Assert.Equal(15, parsed.Slots[1].Sum);
        Assert.Contains(parsed.Warnings, w => w.Contains("inte lika"));
    }

    [Fact]
    public void Parse_ImageWidthZero_ReturnsEmptyWithWarning()
    {
        var tokens = new[] { Tok("4", 100, 100) };
        var parsed = OcrParser.Parse(new OcrResult(tokens, 0, H));

        Assert.Equal(0, parsed.InferredTrackCount);
        Assert.Contains(parsed.Warnings, w => w.Contains("Bildbredd"));
    }
}
