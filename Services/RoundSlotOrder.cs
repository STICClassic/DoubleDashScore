using DoubleDashScore.Models;

namespace DoubleDashScore.Services;

/// <summary>
/// Vilken spelare som satt på vilken position (P1–P4) i en *sparad* omgång.
///
/// <see cref="RoundResult"/> har ingen positionskolumn — kopplingen ligger i
/// radernas insättningsordning: både <c>RoundRepository.CreateRoundAsync</c> och
/// <c>UpdateRoundAsync</c> infogar en rad per kolumn i P1–P4-ordning, så
/// stigande <c>Id</c> bland omgångens levande rader *är* positionsordningen.
/// Ändrar du insättningsordningen i repository:t måste du ändra här också.
/// </summary>
public static class RoundSlotOrder
{
    /// <summary>
    /// Vilka spelar-Id headern ska visa i P1–P4-ordning.
    /// </summary>
    /// <param name="existingResults">
    /// Den redigerade omgångens levande resultatrader — omgångens egen sanning.
    /// Null betyder att en ny omgång skapas; då gäller den globala mappningen.
    /// </param>
    /// <param name="savedGlobalMapping">
    /// Senast sparade "vem sitter var ikväll"-mappningen.
    /// </param>
    public static IReadOnlyList<int>? SlotIdsFor(
        IReadOnlyList<RoundResult>? existingResults,
        IReadOnlyList<int>? savedGlobalMapping) =>
        existingResults is null ? savedGlobalMapping : FromResults(existingResults);

    /// <summary>
    /// Spelar-Id i P1–P4-ordning ur en omgångs resultatrader, eller null om
    /// raderna inte är exakt fyra unika spelare. Null får anroparen att falla
    /// tillbaka på default-mappningen i stället för att visa en påhittad
    /// ordning ovanför siffror som ligger i en annan.
    /// </summary>
    public static IReadOnlyList<int>? FromResults(IEnumerable<RoundResult>? results)
    {
        if (results is null) return null;

        var ids = results
            .Where(r => r.DeletedAt is null)
            .OrderBy(r => r.Id)
            .Select(r => r.PlayerId)
            .ToList();

        if (ids.Count != PlayerPositionMappingCodec.SlotCount) return null;
        if (ids.Any(id => id <= 0)) return null;
        if (ids.Distinct().Count() != ids.Count) return null;

        return ids;
    }
}
