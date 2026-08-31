using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using DoubleDashScore.Models;

namespace DoubleDashScore.Services;

public sealed class ClaudeVisionOcrService : IOcrService
{
    private const string ApiUrl = "https://api.anthropic.com/v1/messages";

    // Verifierad vid Skiva 5-leverans (2026-05). Vision-kvaliteten på poängtavlan
    // är god nog med Sonnet 4.6. Uppdatera först efter manuell test mot bilderna.
    private const string Model = "claude-sonnet-4-6";
    private const string AnthropicVersion = "2023-06-01";

    private readonly HttpClient _http;
    private readonly IApiKeyStore _keys;

    public ClaudeVisionOcrService(HttpClient http, IApiKeyStore keys)
    {
        _http = http;
        _keys = keys;
    }

    public async Task<OcrResult> RecognizeAsync(Stream image, CancellationToken ct = default)
    {
        var apiKey = await _keys.GetAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            Log("no API key stored");
            return OcrResult.Fail(MissingKeyMessage);
        }

        using var ms = new MemoryStream();
        await image.CopyToAsync(ms, ct).ConfigureAwait(false);
        var base64 = Convert.ToBase64String(ms.ToArray());

        var body = new
        {
            model = Model,
            max_tokens = 1024,
            messages = new object[]
            {
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new
                        {
                            type = "image",
                            source = new
                            {
                                type = "base64",
                                media_type = "image/jpeg",
                                data = base64,
                            },
                        },
                        new { type = "text", text = PromptText },
                    },
                },
            },
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, ApiUrl)
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Add("x-api-key", apiKey);
        request.Headers.Add("anthropic-version", AnthropicVersion);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsNetworkFailure(ex, ct))
        {
            Log($"network failure: {ex.GetType().Name}: {ex.Message}");
            return OcrResult.Fail(NetworkErrorMessage);
        }

        using (response)
        {
            var status = (int)response.StatusCode;

            string bodyText;
            try
            {
                bodyText = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (IsNetworkFailure(ex, ct))
            {
                Log($"network failure while reading body (HTTP {status}): {ex.GetType().Name}: {ex.Message}");
                return OcrResult.Fail(NetworkErrorMessage);
            }

            if (!response.IsSuccessStatusCode)
            {
                Log($"HTTP {status} from Anthropic. Body: {bodyText}");
                return OcrResult.Fail(DescribeFailure(status, bodyText));
            }

            try
            {
                return OcrResult.Ok(ParseApiResponse(bodyText));
            }
            catch (InvalidOperationException ex)
            {
                // Ovanligt: 200 OK men innehållet gick inte att tolka. Logga hela
                // svaret — det är enda spåret vi har om formatet ändras.
                Log($"HTTP 200 but unparsable response: {ex.Message}. Body: {bodyText}");
                return OcrResult.Fail(UnexpectedResponseMessage);
            }
        }
    }

    internal const string MissingKeyMessage =
        "OCR-nyckeln saknas. Ange din Anthropic API-nyckel i Inställningar.";

    internal const string QuotaMessage =
        "OCR:n är slut på kvot. Fyll på ditt Anthropic-konto på console.anthropic.com " +
        "och försök igen.";

    internal const string InvalidKeyMessage =
        "OCR-nyckeln accepterades inte. Kontrollera API-nyckeln i Inställningar.";

    internal const string RateLimitMessage =
        "För många OCR-förfrågningar just nu. Vänta en minut och försök igen.";

    internal const string NetworkErrorMessage =
        "Ingen internetanslutning eller timeout. Kontrollera nätet och försök igen. " +
        "Alternativt: fyll i resultaten manuellt.";

    internal const string UnexpectedResponseMessage =
        "OCR:n misslyckades. Fyll i resultaten manuellt. " +
        "Tekniskt fel: oväntat svar från API.";

    /// <summary>Max antal tecken av API:ns felmeddelande som bäddas in i popup:en.</summary>
    private const int TechnicalDetailLength = 100;

    /// <summary>
    /// Ord som pekar på ett betalnings-/kvotproblem. Anthropic svarar ibland 400
    /// (invalid_request_error) i stället för 402 när saldot är slut, så texten
    /// i error.type/error.message får avgöra.
    /// </summary>
    private static readonly string[] BillingMarkers =
    {
        "credit", "billing", "quota", "balance", "payment", "insufficient", "funds",
    };

    private static bool IsNetworkFailure(Exception ex, CancellationToken ct) =>
        ex is HttpRequestException ||
        (ex is TaskCanceledException && !ct.IsCancellationRequested);

    /// <summary>
    /// Statuskod + Anthropics felbody → svenskt meddelande. Ren funktion, testad
    /// separat mot alla HTTP-koder utan att API:t behöver anropas.
    /// </summary>
    internal static string DescribeFailure(int status, string? responseBody)
    {
        var error = ReadApiError(responseBody);

        return status switch
        {
            402 => QuotaMessage,
            401 => InvalidKeyMessage,
            429 => RateLimitMessage,
            400 when LooksLikeBillingProblem(error) => QuotaMessage,
            _ => BuildTechnicalMessage(status, error.Message),
        };
    }

    private static string BuildTechnicalMessage(int status, string? apiMessage)
    {
        var detail = string.IsNullOrWhiteSpace(apiMessage)
            ? string.Empty
            : " " + Truncate(apiMessage!.Trim(), TechnicalDetailLength);
        return $"OCR:n misslyckades. Fyll i resultaten manuellt. Tekniskt fel: {status}{detail}";
    }

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..max] + "…";

    private static bool LooksLikeBillingProblem((string? Type, string? Message) error)
    {
        var haystack = $"{error.Type} {error.Message}".ToLowerInvariant();
        return BillingMarkers.Any(marker => haystack.Contains(marker));
    }

    /// <summary>
    /// Plockar ut error.type och error.message ur Anthropics felformat:
    /// <c>{ "type": "error", "error": { "type": "...", "message": "..." } }</c>.
    /// Icke-JSON eller oväntad form ger (null, null) — då faller meddelandet
    /// tillbaka på enbart statuskoden.
    /// </summary>
    internal static (string? Type, string? Message) ReadApiError(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return (null, null);
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return (null, null);
            if (!doc.RootElement.TryGetProperty("error", out var error)) return (null, null);
            if (error.ValueKind != JsonValueKind.Object) return (null, null);
            return (ReadString(error, "type"), ReadString(error, "message"));
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    private static string? ReadString(JsonElement obj, string property)
    {
        if (!obj.TryGetProperty(property, out var element)) return null;
        if (element.ValueKind != JsonValueKind.String) return null;
        var value = element.GetString();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    /// <summary>Rå felinfo till VS Output — enda spåret när OCR fallerar i fält.</summary>
    private static void Log(string message) =>
        Debug.WriteLine($"[ClaudeVisionOcr] {message}");

    internal static ParsedCounters ParseApiResponse(string apiBody)
    {
        string modelText;
        try
        {
            using var doc = JsonDocument.Parse(apiBody);
            if (!doc.RootElement.TryGetProperty("content", out var content) ||
                content.ValueKind != JsonValueKind.Array ||
                content.GetArrayLength() == 0)
            {
                throw new InvalidOperationException("Oväntat svar från API. Försök igen.");
            }
            if (!content[0].TryGetProperty("text", out var textElement) ||
                textElement.ValueKind != JsonValueKind.String)
            {
                throw new InvalidOperationException("Oväntat svar från API. Försök igen.");
            }
            modelText = textElement.GetString() ?? string.Empty;
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("Oväntat svar från API. Försök igen.");
        }

        return ParseModelJson(modelText);
    }

    internal static ParsedCounters ParseModelJson(string modelText)
    {
        var warnings = new List<string>();
        var trimmed = StripCodeFence(modelText.Trim());
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            warnings.Add("Modellen returnerade tomt svar.");
            return BuildEmpty(warnings);
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(trimmed);
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("Oväntat svar från API. Försök igen.");
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException("Oväntat svar från API. Försök igen.");
            }

            if (!root.TryGetProperty("players", out var playersElement) ||
                playersElement.ValueKind != JsonValueKind.Array)
            {
                warnings.Add("Modellens svar saknade fältet 'players'. Mata in manuellt.");
                AddModelWarnings(root, warnings);
                return BuildEmpty(warnings);
            }

            var slots = new PlayerSlotCounters?[4];
            foreach (var playerElement in playersElement.EnumerateArray())
            {
                if (playerElement.ValueKind != JsonValueKind.Object) continue;
                if (!playerElement.TryGetProperty("slot", out var slotEl)) continue;
                if (slotEl.ValueKind != JsonValueKind.Number) continue;
                if (!slotEl.TryGetInt32(out var slot)) continue;
                if (slot < 1 || slot > 4) continue;

                var first = ReadCount(playerElement, "first");
                var second = ReadCount(playerElement, "second");
                var third = ReadCount(playerElement, "third");
                var fourth = ReadCount(playerElement, "fourth");

                if (first.IsUncertain) warnings.Add($"P{slot} 1:a: osäker avläsning — verifiera manuellt.");
                if (second.IsUncertain) warnings.Add($"P{slot} 2:a: osäker avläsning — verifiera manuellt.");
                if (third.IsUncertain) warnings.Add($"P{slot} 3:e: osäker avläsning — verifiera manuellt.");
                if (fourth.IsUncertain) warnings.Add($"P{slot} 4:e: osäker avläsning — verifiera manuellt.");

                slots[slot - 1] = new PlayerSlotCounters(
                    slot - 1, first.Value, second.Value, third.Value, fourth.Value);
            }

            for (int i = 0; i < 4; i++)
            {
                if (slots[i] is null)
                {
                    warnings.Add($"Slot P{i + 1} saknades i svaret — fyllde med 0.");
                    slots[i] = new PlayerSlotCounters(i, 0, 0, 0, 0);
                }
            }

            var expectedSum = MostCommonSum(slots!);

            var inferred = ReadIntOrZero(root, "total_tracks");
            if (inferred <= 0)
            {
                inferred = expectedSum;
            }

            AddModelWarnings(root, warnings);
            AddSumMismatchWarnings(slots!, expectedSum, warnings);

            return new ParsedCounters(
                slots.Select(s => s!).ToList(),
                inferred,
                warnings);
        }
    }

    private static int MostCommonSum(PlayerSlotCounters[] slots)
    {
        return slots
            .GroupBy(s => s.Sum)
            .OrderByDescending(g => g.Count())
            .ThenByDescending(g => g.Key)
            .First()
            .Key;
    }

    private static void AddSumMismatchWarnings(
        PlayerSlotCounters[] slots, int expectedSum, List<string> warnings)
    {
        for (int i = 0; i < slots.Length; i++)
        {
            var actual = slots[i].Sum;
            if (actual == expectedSum) continue;
            warnings.Add(
                $"P{i + 1}: summan {actual} stämmer inte med övriga ({expectedSum}) — kontrollera 1:or/2:or/3:or/4:or.");
        }
    }

    private static void AddModelWarnings(JsonElement root, List<string> warnings)
    {
        if (!root.TryGetProperty("warnings", out var warningsElement)) return;
        if (warningsElement.ValueKind != JsonValueKind.Array) return;
        foreach (var w in warningsElement.EnumerateArray())
        {
            if (w.ValueKind != JsonValueKind.String) continue;
            var s = w.GetString();
            if (string.IsNullOrWhiteSpace(s)) continue;
            if (IsNoisyWarning(s)) continue;
            warnings.Add(s);
        }
    }

    private static readonly string[] ActionableMarkers =
    {
        "uncertain", "osäker", "osaker", "mismatch", "skiljer", "differ", "differs",
        "inconsistent", "unclear", "could be", "looks like", "saknas", "saknades",
        "kunde inte", "rätta", "ratta", "varning", "warning", "fel",
    };

    private static readonly string[] NoisyMarkers =
    {
        "consistent", "verified", "looks correct", "all correct", "no issues",
        "everything ok", "everything fine", "all rows verified", "all sums match",
    };

    private static readonly Regex SumPattern =
        new(@"sum[\s:=]+\d+", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    internal static bool IsNoisyWarning(string warning)
    {
        var lower = warning.ToLowerInvariant();
        if (ActionableMarkers.Any(m => lower.Contains(m))) return false;
        if (NoisyMarkers.Any(m => lower.Contains(m))) return true;
        if (SumPattern.IsMatch(lower)) return true;
        return false;
    }

    private static int ReadIntOrZero(JsonElement obj, string property)
    {
        if (!obj.TryGetProperty(property, out var el)) return 0;
        if (el.ValueKind != JsonValueKind.Number) return 0;
        if (!el.TryGetInt32(out var v)) return 0;
        return v < 0 ? 0 : v;
    }

    private static (int Value, bool IsUncertain) ReadCount(JsonElement obj, string property)
    {
        if (!obj.TryGetProperty(property, out var el)) return (0, false);
        if (el.ValueKind != JsonValueKind.Number) return (0, false);
        if (!el.TryGetInt32(out var v)) return (0, false);
        if (v < 0) return (0, true);
        return (v, false);
    }

    private static string StripCodeFence(string text)
    {
        if (!text.StartsWith("```")) return text;
        var firstNewline = text.IndexOf('\n');
        if (firstNewline < 0) return text;
        var withoutOpen = text[(firstNewline + 1)..];
        var fenceClose = withoutOpen.LastIndexOf("```", StringComparison.Ordinal);
        return fenceClose < 0 ? withoutOpen : withoutOpen[..fenceClose].TrimEnd();
    }

    private static ParsedCounters BuildEmpty(List<string> warnings)
    {
        var slots = Enumerable.Range(0, 4)
            .Select(i => new PlayerSlotCounters(i, 0, 0, 0, 0))
            .ToList();
        return new ParsedCounters(slots, 0, warnings);
    }

    private const string PromptText = """
You are an OCR assistant for a Mario Kart Double Dash scoreboard. The image shows the Grand Prix results screen after a round, with four player boxes (P1, P2, P3, P4) — arranged either side-by-side or stacked vertically depending on the photo orientation. Identify the player boxes by their "P1"/"P2"/"P3"/"P4" labels.

Each player box shows:
- Player ID (P1/P2/P3/P4)
- Two character icons
- Four counters: how many tracks the player finished in 1st, 2nd, 3rd, and 4th place during the round.

CRITICAL INSTRUCTIONS — READ CAREFULLY:

1. READ EACH CELL INDEPENDENTLY. Do not adjust numbers to make column sums consistent. If you see "01" in P2 row 3 but it makes P2's total unusual, report 1 with a warning. Never modify a digit to fit an expected total. Honesty per cell beats consistency across cells.

2. NUMBERS ARE 2-DIGIT WITH LEADING ZERO (01, 02, 03, 04, 05, 06, 07, 08, 09, 10, 11, 12, 13, 14, 15, 16). Report them as integers without the leading zero (01 → 1, 12 → 12). Do NOT report 0 unless the cell shows "00".

3. FOR EACH CELL WHERE YOU ARE UNCERTAIN: set the value to -1 (NOT 0, NOT a guess) and add a warning string identifying the cell. Example: "P2 3rd: uncertain, looks like 1 or 4". Do NOT guess to maintain column consistency. -1 is the signal that means "I cannot read this confidently".

4. COMMON CONFUSIONS IN THIS FONT — look carefully at these pairs:
   - 1 vs 4 (the "1" has a curved/looped top in this stylised font)
   - 1 vs 7
   - 3 vs 5
   - 6 vs 8

5. DO NOT ASSUME TOTAL TRACKS. Count what you actually see. If P1's four cells sum to 14, report total_tracks=14 even if other players sum to different totals. The round may be partial (fewer than 16 tracks) or you may misread cells — in either case, report what you observe per player, do not default to 16.

6. VERIFY ROW/COLUMN SUMS BEFORE RETURNING JSON. After reading every cell, compute each player's sum (first + second + third + fourth). All four player sums SHOULD be equal because every track produces exactly one finisher per position. If any player's sum differs from the others, add a specific warning identifying which player deviates — for example "P3 sum (14) differs from others (16); re-check P3's cells". Do NOT modify your readings to make sums match; report what you see and flag the discrepancy.

7. DO NOT EMIT NOISE-LEVEL WARNINGS. Only include warnings that require user action: uncertain cells, sum mismatches, or wrong-image alerts. Do NOT add positive confirmations like "P2 sum: 16, consistent" or "all rows verified" — silence means everything looked fine.

Return STRICT JSON in exactly this format. No code fences, no commentary, no text before or after:

{
  "players": [
    {"slot": 1, "first": <count or -1>, "second": <count or -1>, "third": <count or -1>, "fourth": <count or -1>},
    {"slot": 2, "first": ..., "second": ..., "third": ..., "fourth": ...},
    {"slot": 3, "first": ..., "second": ..., "third": ..., "fourth": ...},
    {"slot": 4, "first": ..., "second": ..., "third": ..., "fourth": ...}
  ],
  "total_tracks": <the per-player sum you actually observed; do NOT default to 16>,
  "warnings": [<short actionable strings only — uncertain cells, sum mismatches, wrong image>]
}

Rules:
- slot 1 = P1, slot 2 = P2, slot 3 = P3, slot 4 = P4.
- If the image is not a Mario Kart Double Dash scoreboard: return all values as 0 and add a warning.
- Return JSON only. Nothing else.
""";
}
