namespace DoubleDashScore.Services;

/// <summary>
/// Avgör om en sparad omgång får skriva om den persisterade position-till-
/// spelare-mappningen ("vem sitter var ikväll").
///
/// Bara en ny omgång eller en redigering av databasens *senaste* omgång får
/// göra det. Redigerar man en gammal omgång är mappningen där historisk — att
/// låta den skriva över dagens mappning vore tyst korruption som är svår att
/// upptäcka i vardagen.
/// </summary>
public static class MappingPersistenceRule
{
    /// <param name="editedRoundId">
    /// Omgången som redigeras. Null eller 0 betyder att en ny omgång skapas —
    /// den blir databasens senaste i och med att den sparas.
    /// </param>
    /// <param name="latestRoundId">
    /// Högsta <c>Round.Id</c> i databasen med soft-deletade bortfiltrerade,
    /// eller null om det inte finns någon omgång alls.
    /// </param>
    public static bool ShouldPersist(int? editedRoundId, int? latestRoundId)
    {
        if (editedRoundId is null or <= 0) return true;
        return latestRoundId is not null && editedRoundId == latestRoundId;
    }
}
