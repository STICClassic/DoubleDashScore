using System.Text.Json;

namespace DoubleDashScore.Services;

/// <summary>
/// Lagrar vilken spelare som satt på vilken GameCube-position (P1–P4) vid
/// senaste inmatningen, så nästa inmatning öppnar med samma mappning.
/// Delas av OCR-förhandsgranskningen och den manuella inmatningen — det är
/// samma "vem sitter var ikväll"-koncept i båda flödena.
/// Interfacet finns för att hålla ViewModel:en fri från MAUI-beroenden —
/// samma mönster som <see cref="IApiKeyStore"/>/<see cref="IGitHubTokenStore"/>.
/// </summary>
public interface IPlayerPositionMappingStore
{
    /// <summary>Sparade spelar-Id i P1–P4-ordning, eller null om inget sparats.</summary>
    IReadOnlyList<int>? Get();

    void Set(IReadOnlyList<int> playerIds);
}

/// <summary>
/// JSON-kodning av mappningen: en array av 4 spelar-Id i P1–P4-ordning.
/// Ren funktion, separerad från lagringen så den kan enhetstestas.
/// </summary>
public static class PlayerPositionMappingCodec
{
    public const int SlotCount = 4;

    public static string Serialize(IReadOnlyList<int> playerIds) =>
        JsonSerializer.Serialize(playerIds);

    /// <summary>
    /// Läser tillbaka en sparad mappning. Allt som inte är exakt fyra positiva
    /// unika Id:n avvisas — då faller anroparen tillbaka på default-mappningen
    /// i stället för att applicera skräp.
    /// </summary>
    public static IReadOnlyList<int>? TryDeserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        int[]? ids;
        try
        {
            ids = JsonSerializer.Deserialize<int[]>(json);
        }
        catch (JsonException)
        {
            return null;
        }

        if (ids is null || ids.Length != SlotCount) return null;
        if (ids.Any(id => id <= 0)) return null;
        if (ids.Distinct().Count() != SlotCount) return null;

        return ids;
    }
}
