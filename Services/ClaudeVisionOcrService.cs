using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DoubleDashScore.Models;

namespace DoubleDashScore.Services;

public sealed class ClaudeVisionOcrService : IOcrService
{
    private const string ApiUrl = "https://api.anthropic.com/v1/messages";
    private const string Model = "claude-haiku-4-5-20251001";
    private const string AnthropicVersion = "2023-06-01";

    private readonly HttpClient _http;
    private readonly IApiKeyStore _keys;

    public ClaudeVisionOcrService(HttpClient http, IApiKeyStore keys)
    {
        _http = http;
        _keys = keys;
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

        var bodyText = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return ParseApiResponse(bodyText);
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

                slots[slot - 1] = new PlayerSlotCounters(
                    slot - 1,
                    ReadIntOrZero(playerElement, "first"),
                    ReadIntOrZero(playerElement, "second"),
                    ReadIntOrZero(playerElement, "third"),
                    ReadIntOrZero(playerElement, "fourth"));
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
        return el.TryGetInt32(out var v) ? v : 0;
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
Du är en OCR-assistent för Mario Kart Double Dash. Bilden visar spelets poängtavla efter en omgång (Grand Prix-resultat). Poängtavlan har fyra spelarrutor (P1, P2, P3, P4) — antingen sida vid sida eller staplade vertikalt beroende på hur bilden är vriden. Identifiera spelaretiketterna (P1/P2/P3/P4) på bilden för att bestämma orienteringen.

Varje spelarruta visar:
- Spelar-ID (P1/P2/P3/P4)
- Två karaktärsikoner
- Fyra siffror som representerar antal banor spelaren kom 1:a, 2:a, 3:e respektive 4:e på.

Siffrorna är vanligtvis tvåsiffriga med ledande nolla (t.ex. "06" = 6).

Returnera STRIKT JSON i exakt detta format. Inga kodblock, inga kommentarer, ingen text före eller efter:

{
  "players": [
    {"slot": 1, "first": <antal 1:or>, "second": <antal 2:or>, "third": <antal 3:or>, "fourth": <antal 4:or>},
    {"slot": 2, "first": ..., "second": ..., "third": ..., "fourth": ...},
    {"slot": 3, "first": ..., "second": ..., "third": ..., "fourth": ...},
    {"slot": 4, "first": ..., "second": ..., "third": ..., "fourth": ...}
  ],
  "total_tracks": <summan av räknarna per spelare, vanligtvis 16>,
  "warnings": [<svenska strängar med eventuella problem>]
}

Regler:
- slot 1 = P1, slot 2 = P2, slot 3 = P3, slot 4 = P4.
- Om en siffra inte syns tydligt: returnera 0 och lägg till en varning som beskriver vilken slot och plats det gällde.
- Om bilden inte verkar vara en Mario Kart Double Dash-poängtavla: returnera räknare på 0 och en varning som förklarar.
- Varje spelares summa (first+second+third+fourth) bör vara samma; sätt total_tracks till den summan. Om summorna skiljer sig: rapportera det i warnings men sätt total_tracks till det vanligaste värdet.
- Returnera bara JSON — ingenting annat.
""";
}
