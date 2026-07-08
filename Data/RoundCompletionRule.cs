namespace DoubleDashScore.Data;

// Enda källan till "komplett omgång"-regeln: en omgång är komplett endast om
// den har exakt 16 banor OCH exakt 4 resultatrader (en per aktiv spelare).
// Ligger i Data eftersom regeln arbetar på råvärden (int, int) — både
// RoundDetail.IsComplete (hydraterade objekt) och GameNightRepository
// .GetSummariesAsync (råvärden från SQL) anropar den i stället för att
// duplicera villkoret. Ändras regeln, ändra den här — konsumenterna följer med.
public static class RoundCompletionRule
{
    public const int RequiredTrackCount = 16;
    public const int RequiredResultsCount = 4;

    public static bool IsComplete(int trackCount, int resultsCount)
        => trackCount == RequiredTrackCount && resultsCount == RequiredResultsCount;
}
