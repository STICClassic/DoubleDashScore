using System.Net;
using System.Text;
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
    public void ParseApiResponse_MissingTotalTracks_FallsBackToMostCommonSum()
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

    [Fact]
    public void ParseApiResponse_SumMismatch_WarnsPerDeviatingPlayer()
    {
        var modelText = """
        {
          "players": [
            {"slot": 1, "first": 4, "second": 4, "third": 4, "fourth": 4},
            {"slot": 2, "first": 4, "second": 4, "third": 4, "fourth": 2},
            {"slot": 3, "first": 4, "second": 4, "third": 4, "fourth": 4},
            {"slot": 4, "first": 4, "second": 4, "third": 4, "fourth": 4}
          ],
          "total_tracks": 16,
          "warnings": []
        }
        """;
        var parsed = ClaudeVisionOcrService.ParseApiResponse(Wrap(modelText));

        Assert.Contains(parsed.Warnings, w =>
            w.Contains("P2") && w.Contains("14") && w.Contains("16") && w.Contains("stämmer inte"));
        Assert.DoesNotContain(parsed.Warnings, w => w.Contains("P1:"));
        Assert.DoesNotContain(parsed.Warnings, w => w.Contains("P3:"));
        Assert.DoesNotContain(parsed.Warnings, w => w.Contains("P4:"));
    }

    [Fact]
    public void ParseApiResponse_AllSumsMatch_NoMismatchWarning()
    {
        var modelText = """
        {
          "players": [
            {"slot": 1, "first": 4, "second": 4, "third": 4, "fourth": 4},
            {"slot": 2, "first": 4, "second": 4, "third": 4, "fourth": 4},
            {"slot": 3, "first": 4, "second": 4, "third": 4, "fourth": 4},
            {"slot": 4, "first": 4, "second": 4, "third": 4, "fourth": 4}
          ],
          "total_tracks": 16,
          "warnings": []
        }
        """;
        var parsed = ClaudeVisionOcrService.ParseApiResponse(Wrap(modelText));

        Assert.DoesNotContain(parsed.Warnings, w => w.Contains("stämmer inte"));
    }

    [Theory]
    [InlineData("P2 sum: 16, consistent")]
    [InlineData("All rows verified")]
    [InlineData("Looks correct")]
    [InlineData("P3 sum: 14")]
    [InlineData("sum=16 for all players")]
    public void IsNoisyWarning_FiltersPositiveConfirmations(string warning)
    {
        Assert.True(ClaudeVisionOcrService.IsNoisyWarning(warning));
    }

    [Theory]
    [InlineData("P2 3rd: uncertain, looks like 1 or 4")]
    [InlineData("P3 sum (14) differs from others (16)")]
    [InlineData("Slot P1 kunde inte avläsas")]
    [InlineData("Image does not look like a scoreboard")]
    [InlineData("P1 1:a osäker avläsning")]
    public void IsNoisyWarning_KeepsActionableMessages(string warning)
    {
        Assert.False(ClaudeVisionOcrService.IsNoisyWarning(warning));
    }

    [Fact]
    public void ParseApiResponse_DropsNoisyModelWarnings_KeepsActionable()
    {
        var modelText = """
        {
          "players": [
            {"slot": 1, "first": 4, "second": 4, "third": 4, "fourth": 4},
            {"slot": 2, "first": 4, "second": 4, "third": 4, "fourth": 4},
            {"slot": 3, "first": 4, "second": 4, "third": 4, "fourth": 4},
            {"slot": 4, "first": 4, "second": 4, "third": 4, "fourth": 4}
          ],
          "total_tracks": 16,
          "warnings": [
            "P1 sum: 16, consistent",
            "P3 2nd: uncertain, looks like 4 or 1"
          ]
        }
        """;
        var parsed = ClaudeVisionOcrService.ParseApiResponse(Wrap(modelText));

        Assert.DoesNotContain(parsed.Warnings, w => w.Contains("consistent"));
        Assert.Contains(parsed.Warnings, w => w.Contains("P3 2nd"));
    }

    // --- Skiva 28: felkategorisering ------------------------------------

    private static ClaudeVisionOcrService Build(FakeHandler handler, string? key = "sk-test")
        => new(new HttpClient(handler), new FakeKeyStore(key));

    private static Stream Image() => new MemoryStream(new byte[] { 1, 2, 3 });

    private static HttpResponseMessage Error(HttpStatusCode status, string type, string message)
        => new(status)
        {
            Content = new StringContent(
                $$$"""
                {"type": "error", "error": {"type": "{{{type}}}", "message": "{{{message}}}"}}
                """,
                Encoding.UTF8,
                "application/json"),
        };

    [Fact]
    public async Task RecognizeAsync_HappyPath_ReturnsSuccessWithCounters()
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
        var handler = new FakeHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(Wrap(modelText), Encoding.UTF8, "application/json"),
        });

        var result = await Build(handler).RecognizeAsync(Image());

        Assert.True(result.Success);
        Assert.Null(result.ErrorMessage);
        Assert.NotNull(result.Counters);
        Assert.Equal(16, result.Counters!.InferredTrackCount);
        Assert.Equal(6, result.Counters.Slots[0].FirstPlaces);
    }

    [Fact]
    public async Task RecognizeAsync_UtanApiNyckel_GerFelUtanAttAnropaApi()
    {
        var handler = new FakeHandler();

        var result = await Build(handler, key: null).RecognizeAsync(Image());

        Assert.False(result.Success);
        Assert.Equal(ClaudeVisionOcrService.MissingKeyMessage, result.ErrorMessage);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task RecognizeAsync_402_GerKvotmeddelande()
    {
        var handler = new FakeHandler(Error(
            HttpStatusCode.PaymentRequired, "invalid_request_error", "Your credit balance is too low"));

        var result = await Build(handler).RecognizeAsync(Image());

        Assert.False(result.Success);
        Assert.Equal(ClaudeVisionOcrService.QuotaMessage, result.ErrorMessage);
        Assert.Contains("console.anthropic.com", result.ErrorMessage);
    }

    [Fact]
    public async Task RecognizeAsync_401_GerNyckelmeddelande()
    {
        var handler = new FakeHandler(Error(
            HttpStatusCode.Unauthorized, "authentication_error", "invalid x-api-key"));

        var result = await Build(handler).RecognizeAsync(Image());

        Assert.False(result.Success);
        Assert.Equal(ClaudeVisionOcrService.InvalidKeyMessage, result.ErrorMessage);
    }

    [Fact]
    public async Task RecognizeAsync_429_GerRateLimitmeddelande()
    {
        var handler = new FakeHandler(Error(
            HttpStatusCode.TooManyRequests, "rate_limit_error", "rate limit exceeded"));

        var result = await Build(handler).RecognizeAsync(Image());

        Assert.False(result.Success);
        Assert.Equal(ClaudeVisionOcrService.RateLimitMessage, result.ErrorMessage);
    }

    [Theory]
    [InlineData("Your credit balance is too low to access the API")]
    [InlineData("This organization has exceeded its monthly quota")]
    [InlineData("Billing is not configured for this workspace")]
    [InlineData("Insufficient funds")]
    public async Task RecognizeAsync_400MedBetalningstext_BehandlasSomKvot(string apiMessage)
    {
        var handler = new FakeHandler(Error(
            HttpStatusCode.BadRequest, "invalid_request_error", apiMessage));

        var result = await Build(handler).RecognizeAsync(Image());

        Assert.False(result.Success);
        Assert.Equal(ClaudeVisionOcrService.QuotaMessage, result.ErrorMessage);
    }

    [Fact]
    public async Task RecognizeAsync_400UtanBetalningstext_GerTekniskFallback()
    {
        var handler = new FakeHandler(Error(
            HttpStatusCode.BadRequest, "invalid_request_error", "max_tokens: must be >= 1"));

        var result = await Build(handler).RecognizeAsync(Image());

        Assert.False(result.Success);
        Assert.Contains("Fyll i resultaten manuellt", result.ErrorMessage);
        Assert.Contains("Tekniskt fel: 400", result.ErrorMessage);
        Assert.Contains("max_tokens", result.ErrorMessage);
    }

    [Theory]
    [InlineData(405)]
    [InlineData(500)]
    [InlineData(503)]
    [InlineData(529)]
    public async Task RecognizeAsync_OvrigaHttpFel_GerTekniskFallbackMedKod(int status)
    {
        var handler = new FakeHandler(Error(
            (HttpStatusCode)status, "api_error", "Internal server error"));

        var result = await Build(handler).RecognizeAsync(Image());

        Assert.False(result.Success);
        Assert.Contains($"Tekniskt fel: {status} Internal server error", result.ErrorMessage);
    }

    [Fact]
    public void DescribeFailure_UtanJsonBody_GerBaraStatuskod()
    {
        var message = ClaudeVisionOcrService.DescribeFailure(502, "<html>Bad Gateway</html>");

        Assert.Equal(
            "OCR:n misslyckades. Fyll i resultaten manuellt. Tekniskt fel: 502", message);
    }

    [Fact]
    public void DescribeFailure_LangtApiMeddelande_Kortas()
    {
        var longMessage = new string('x', 250);
        var body = $$$"""{"type": "error", "error": {"type": "api_error", "message": "{{{longMessage}}}"}}""";

        var message = ClaudeVisionOcrService.DescribeFailure(500, body);

        Assert.Contains(new string('x', 100) + "…", message);
        Assert.DoesNotContain(new string('x', 101), message);
    }

    [Fact]
    public void ReadApiError_LaserTypOchMeddelande()
    {
        var (type, message) = ClaudeVisionOcrService.ReadApiError(
            """{"type": "error", "error": {"type": "not_found_error", "message": "model not found"}}""");

        Assert.Equal("not_found_error", type);
        Assert.Equal("model not found", message);
    }

    [Fact]
    public async Task RecognizeAsync_Natverksfel_GerNatverksmeddelande()
    {
        var handler = new FakeHandler { Throw = new HttpRequestException("no route to host") };

        var result = await Build(handler).RecognizeAsync(Image());

        Assert.False(result.Success);
        Assert.Equal(ClaudeVisionOcrService.NetworkErrorMessage, result.ErrorMessage);
    }

    [Fact]
    public async Task RecognizeAsync_Timeout_GerNatverksmeddelande()
    {
        // HttpClient kastar TaskCanceledException vid timeout, utan att vår
        // CancellationToken är avbruten — det ska läsas som nätverksfel.
        var handler = new FakeHandler { Throw = new TaskCanceledException("timeout") };

        var result = await Build(handler).RecognizeAsync(Image());

        Assert.False(result.Success);
        Assert.Equal(ClaudeVisionOcrService.NetworkErrorMessage, result.ErrorMessage);
    }

    [Fact]
    public async Task RecognizeAsync_AnvandarAvbrott_KastasVidare()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var handler = new FakeHandler { Throw = new TaskCanceledException("cancelled") };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Build(handler).RecognizeAsync(Image(), cts.Token));
    }

    [Fact]
    public async Task RecognizeAsync_TvahundraMedOtolkbartSvar_GerTekniskFallback()
    {
        var handler = new FakeHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("inte json alls", Encoding.UTF8, "application/json"),
        });

        var result = await Build(handler).RecognizeAsync(Image());

        Assert.False(result.Success);
        Assert.Equal(ClaudeVisionOcrService.UnexpectedResponseMessage, result.ErrorMessage);
    }

    private sealed class FakeKeyStore : IApiKeyStore
    {
        private string? _key;

        public FakeKeyStore(string? key) => _key = key;

        public Task<string?> GetAsync(CancellationToken ct = default) => Task.FromResult(_key);

        public Task SetAsync(string? key, CancellationToken ct = default)
        {
            _key = key;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses;

        public FakeHandler(params HttpResponseMessage[] responses) =>
            _responses = new Queue<HttpResponseMessage>(responses);

        public Exception? Throw { get; init; }

        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            if (Throw is not null) throw Throw;

            return Task.FromResult(_responses.Count > 0
                ? _responses.Dequeue()
                : new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{}", Encoding.UTF8, "application/json"),
                });
        }
    }
}
