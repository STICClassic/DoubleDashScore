using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using DoubleDashScore.Models;

namespace DoubleDashScore.Services;

public sealed class ClaudeVisionOcrService : IOcrService
{
    private const string ApiUrl = "https://api.anthropic.com/v1/messages";
    private const string Model = "claude-sonnet-4-6";
    private const string AnthropicVersion = "2023-06-01";

    private readonly HttpClient _http;
    private readonly IApiKeyStore _keys;
    private readonly IOcrDiagnosticsSink _diag;

    public ClaudeVisionOcrService(HttpClient http, IApiKeyStore keys, IOcrDiagnosticsSink diag)
    {
        _http = http;
        _keys = keys;
        _diag = diag;
    }

    public async Task<ParsedCounters> RecognizeAsync(Stream image, CancellationToken ct = default)
    {
        var apiKey = await _keys.GetAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("API-nyckel saknas. Sätt i Inställningar.");
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
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException("Kunde inte nå API. Försök igen.", ex);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new InvalidOperationException("Tidsgräns nåddes. Försök igen.");
        }

        var bodyText = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
        SaveDebug($"api-{timestamp}-response.txt",
            $"HTTP {(int)response.StatusCode} {response.StatusCode}\n\n{bodyText}");

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new InvalidOperationException("Ogiltig API-nyckel. Kontrollera i Inställningar.");
        }
        if ((int)response.StatusCode == 429)
        {
            throw new InvalidOperationException("För många förfrågningar. Vänta lite.");
        }
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"API-fel ({(int)response.StatusCode}). Försök igen.");
        }

        ParsedCounters parsed;
        try
        {
            parsed = ParseApiResponse(bodyText);
        }
        catch (Exception parseEx)
        {
            SaveDebug($"api-{timestamp}-parse-error.txt",
                $"{parseEx.GetType().Name}: {parseEx.Message}");
            throw;
        }

        SaveDebug($"api-{timestamp}-parsed.json",
            JsonSerializer.Serialize(parsed, new JsonSerializerOptions { WriteIndented = true }));
        return parsed;
    }

    private static readonly Regex ApiKeyPattern = new(@"sk-ant-[A-Za-z0-9_\-]+", RegexOptions.Compiled);

    private void SaveDebug(string filename, string content)
    {
        var redacted = ApiKeyPattern.Replace(content, "sk-ant-REDACTED");
        _diag.Save(filename, redacted);
    }

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

            var inferred = ReadIntOrZero(root, "total_tracks");
            if (inferred <= 0)
            {
                inferred = slots[0]!.Sum;
            }

            AddModelWarnings(root, warnings);

            return new ParsedCounters(
                slots.Select(s => s!).ToList(),
                inferred,
                warnings);
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
            if (!string.IsNullOrWhiteSpace(s)) warnings.Add(s);
        }
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

Return STRICT JSON in exactly this format. No code fences, no commentary, no text before or after:

{
  "players": [
    {"slot": 1, "first": <count or -1>, "second": <count or -1>, "third": <count or -1>, "fourth": <count or -1>},
    {"slot": 2, "first": ..., "second": ..., "third": ..., "fourth": ...},
    {"slot": 3, "first": ..., "second": ..., "third": ..., "fourth": ...},
    {"slot": 4, "first": ..., "second": ..., "third": ..., "fourth": ...}
  ],
  "total_tracks": <usually 16; set to most common per-player sum if confident>,
  "warnings": [<short strings identifying any uncertain cells or other issues>]
}

Rules:
- slot 1 = P1, slot 2 = P2, slot 3 = P3, slot 4 = P4.
- If the image is not a Mario Kart Double Dash scoreboard: return all values as 0 and add a warning.
- Return JSON only. Nothing else.
""";
}
