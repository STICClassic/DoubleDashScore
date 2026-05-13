using DoubleDashScore.Models;
using DoubleDashScore.Services;
using Xunit;

namespace DoubleDashScore.Tests;

public class ClaudeVisionOcrServiceTests
{
    private static string Wrap(string modelJsonText)
    {
        var escaped = System.Text.Json.JsonSerializer.Serialize(modelJsonText);
        return $$"""
        {
            "id": "msg_test",
            "type": "message",
            "role": "assistant",
            "model": "claude-haiku-4-5-20251001",
            "content": [{"type": "text", "text": {{escaped}}}],
            "stop_reason": "end_turn"
        }
        """;
    }

    [Fact]
    public void ParseApiResponse_HappyPath_BuildsExactCounters()
    {
        var modelText = """
        {
          "players": [
            {"slot": 1, "first": 6, "second": 4, "third": 4, "fourth": 2},
            {"slot": 2, "first": 2, "second": 5, "third": 3, "fourth": 6},
            {"slot": 3, "first": 5, "second": 5, "third": 3, "fourth": 3},
            {"slot": 4, "first": 3, "second": 2, "third": 6, "fourth": 5}
          ],
          "total_tracks": 16,
          "warnings": []
        }
        """;
        var apiBody = Wrap(modelText);

        var parsed = ClaudeVisionOcrService.ParseApiResponse(apiBody);

        Assert.Equal(16, parsed.InferredTrackCount);
        Assert.Empty(parsed.Warnings);
        Assert.Equal(new PlayerSlotCounters(0, 6, 4, 4, 2), parsed.Slots[0]);
        Assert.Equal(new PlayerSlotCounters(1, 2, 5, 3, 6), parsed.Slots[1]);
        Assert.Equal(new PlayerSlotCounters(2, 5, 5, 3, 3), parsed.Slots[2]);
        Assert.Equal(new PlayerSlotCounters(3, 3, 2, 6, 5), parsed.Slots[3]);
    }

    [Fact]
    public void ParseApiResponse_WithModelWarnings_PropagatesWarnings()
    {
        var modelText = """
        {
          "players": [
            {"slot": 1, "first": 0, "second": 0, "third": 0, "fourth": 0},
            {"slot": 2, "first": 4, "second": 4, "third": 4, "fourth": 4},
            {"slot": 3, "first": 4, "second": 4, "third": 4, "fourth": 4},
            {"slot": 4, "first": 4, "second": 4, "third": 4, "fourth": 4}
          ],
          "total_tracks": 16,
          "warnings": ["Slot P1: kunde inte avläsa siffrorna tydligt."]
        }
        """;
        var parsed = ClaudeVisionOcrService.ParseApiResponse(Wrap(modelText));

        Assert.Contains(parsed.Warnings, w => w.Contains("Slot P1"));
        Assert.Equal(0, parsed.Slots[0].Sum);
    }

    [Fact]
    public void ParseApiResponse_StripsCodeFence()
    {
        var modelText = "```json\n{\n  \"players\": [\n    {\"slot\": 1, \"first\": 4, \"second\": 4, \"third\": 4, \"fourth\": 4},\n    {\"slot\": 2, \"first\": 4, \"second\": 4, \"third\": 4, \"fourth\": 4},\n    {\"slot\": 3, \"first\": 4, \"second\": 4, \"third\": 4, \"fourth\": 4},\n    {\"slot\": 4, \"first\": 4, \"second\": 4, \"third\": 4, \"fourth\": 4}\n  ],\n  \"total_tracks\": 16,\n  \"warnings\": []\n}\n```";

        var parsed = ClaudeVisionOcrService.ParseApiResponse(Wrap(modelText));

        Assert.Equal(16, parsed.InferredTrackCount);
        Assert.Equal(4, parsed.Slots[0].FirstPlaces);
    }

    [Fact]
    public void ParseApiResponse_MissingPlayersField_ReturnsEmptyWithWarning()
    {
        var modelText = """{"warnings": ["Bilden verkar inte vara en poängtavla."]}""";

        var parsed = ClaudeVisionOcrService.ParseApiResponse(Wrap(modelText));

        Assert.All(parsed.Slots, s => Assert.Equal(0, s.Sum));
        Assert.Contains(parsed.Warnings, w => w.Contains("saknade") || w.Contains("players"));
        Assert.Contains(parsed.Warnings, w => w.Contains("poängtavla"));
    }

    [Fact]
    public void ParseApiResponse_PartialSlots_FillsMissingWithZerosAndWarns()
    {
        var modelText = """
        {
          "players": [
            {"slot": 1, "first": 4, "second": 4, "third": 4, "fourth": 4},
            {"slot": 3, "first": 4, "second": 4, "third": 4, "fourth": 4}
          ],
          "total_tracks": 16
        }
        """;
        var parsed = ClaudeVisionOcrService.ParseApiResponse(Wrap(modelText));

        Assert.Equal(16, parsed.Slots[0].Sum);
        Assert.Equal(0, parsed.Slots[1].Sum);
        Assert.Equal(16, parsed.Slots[2].Sum);
        Assert.Equal(0, parsed.Slots[3].Sum);
        Assert.Contains(parsed.Warnings, w => w.Contains("P2"));
        Assert.Contains(parsed.Warnings, w => w.Contains("P4"));
    }

    [Fact]
    public void ParseApiResponse_MalformedModelJson_Throws()
    {
        var apiBody = Wrap("not really json {{{{");

        var ex = Assert.Throws<InvalidOperationException>(
            () => ClaudeVisionOcrService.ParseApiResponse(apiBody));
        Assert.Contains("Oväntat", ex.Message);
    }

    [Fact]
    public void ParseApiResponse_TopLevelMalformed_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => ClaudeVisionOcrService.ParseApiResponse("garbage not json"));
        Assert.Contains("Oväntat", ex.Message);
    }

    [Fact]
    public void ParseApiResponse_EmptyContent_Throws()
    {
        var apiBody = """{"id":"msg","content":[]}""";

        var ex = Assert.Throws<InvalidOperationException>(
            () => ClaudeVisionOcrService.ParseApiResponse(apiBody));
        Assert.Contains("Oväntat", ex.Message);
    }

    [Fact]
    public void ParseApiResponse_MissingContentField_Throws()
    {
        var apiBody = """{"id":"msg","stop_reason":"end_turn"}""";

        var ex = Assert.Throws<InvalidOperationException>(
            () => ClaudeVisionOcrService.ParseApiResponse(apiBody));
        Assert.Contains("Oväntat", ex.Message);
    }

    [Fact]
    public void ParseApiResponse_EmptyModelText_ReturnsEmptyWithWarning()
    {
        var apiBody = Wrap("");

        var parsed = ClaudeVisionOcrService.ParseApiResponse(apiBody);

        Assert.All(parsed.Slots, s => Assert.Equal(0, s.Sum));
        Assert.Contains(parsed.Warnings, w => w.Contains("tomt"));
    }

    [Fact]
    public void ParseApiResponse_UncertainCell_AsMinusOne_BecomesZeroWithWarning()
    {
        var modelText = """
        {
          "players": [
            {"slot": 1, "first": 6, "second": 4, "third": 4, "fourth": 2},
            {"slot": 2, "first": 2, "second": 5, "third": -1, "fourth": 6},
            {"slot": 3, "first": 5, "second": 5, "third": 3, "fourth": 3},
            {"slot": 4, "first": 3, "second": 2, "third": 6, "fourth": 5}
          ],
          "total_tracks": 16,
          "warnings": ["P2 3rd: uncertain, looks like 1 or 4"]
        }
        """;
        var parsed = ClaudeVisionOcrService.ParseApiResponse(Wrap(modelText));

        Assert.Equal(0, parsed.Slots[1].ThirdPlaces);
        Assert.Equal(2, parsed.Slots[1].FirstPlaces);
        Assert.Equal(5, parsed.Slots[1].SecondPlaces);
        Assert.Equal(6, parsed.Slots[1].FourthPlaces);
        Assert.Contains(parsed.Warnings, w => w.Contains("P2 3:e") && w.Contains("osäker"));
        Assert.Contains(parsed.Warnings, w => w.Contains("P2 3rd"));
    }

    [Fact]
    public void ParseApiResponse_MultipleUncertainCells_GeneratesMultipleWarnings()
    {
        var modelText = """
        {
          "players": [
            {"slot": 1, "first": -1, "second": -1, "third": 4, "fourth": 2},
            {"slot": 2, "first": 2, "second": 5, "third": 3, "fourth": -1},
            {"slot": 3, "first": 5, "second": 5, "third": 3, "fourth": 3},
            {"slot": 4, "first": 3, "second": 2, "third": 6, "fourth": 5}
          ],
          "total_tracks": 16
        }
        """;
        var parsed = ClaudeVisionOcrService.ParseApiResponse(Wrap(modelText));

        Assert.Equal(0, parsed.Slots[0].FirstPlaces);
        Assert.Equal(0, parsed.Slots[0].SecondPlaces);
        Assert.Equal(0, parsed.Slots[1].FourthPlaces);
        Assert.Contains(parsed.Warnings, w => w.Contains("P1 1:a"));
        Assert.Contains(parsed.Warnings, w => w.Contains("P1 2:a"));
        Assert.Contains(parsed.Warnings, w => w.Contains("P2 4:e"));
    }

    [Fact]
    public void ParseApiResponse_MissingTotalTracks_FallsBackToFirstSlotSum()
    {
        var modelText = """
        {
          "players": [
            {"slot": 1, "first": 5, "second": 5, "third": 5, "fourth": 5},
            {"slot": 2, "first": 5, "second": 5, "third": 5, "fourth": 5},
            {"slot": 3, "first": 5, "second": 5, "third": 5, "fourth": 5},
            {"slot": 4, "first": 5, "second": 5, "third": 5, "fourth": 5}
          ]
        }
        """;
        var parsed = ClaudeVisionOcrService.ParseApiResponse(Wrap(modelText));

        Assert.Equal(20, parsed.InferredTrackCount);
    }
}
