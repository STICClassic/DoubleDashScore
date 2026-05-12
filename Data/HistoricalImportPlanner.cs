using DoubleDashScore.Models;

namespace DoubleDashScore.Data;

/// Ren beslutsfunktion för import: tar parsed Excel-data + vilka NightNumbers
/// som redan finns i DB + en overwrite-flagga och bestämmer vilka rader som
/// ska raderas och vilka som ska skrivas. Repository:t använder den här inuti
/// sin transaktion. Testbar utan DB.
public static class HistoricalImportPlanner
{
    public static HistoricalImportPlan Plan(
        ParsedExcelImport parsed,
        IReadOnlySet<int> existingNightNumbers,
        bool overwrite)
    {
        ArgumentNullException.ThrowIfNull(parsed);
        ArgumentNullException.ThrowIfNull(existingNightNumbers);

        var fileNightNumbers = parsed.NightAggregates
            .Select(a => a.NightNumber)
            .Distinct()
            .ToHashSet();

        var toReplace = new List<int>();
        int nightsInserted = 0;
        int nightsOverwritten = 0;
        int nightsSkipped = 0;

        foreach (var nightNumber in fileNightNumbers.OrderBy(n => n))
        {
            var exists = existingNightNumbers.Contains(nightNumber);
            if (exists && !overwrite)
            {
                nightsSkipped++;
            }
            else if (exists)
            {
                toReplace.Add(nightNumber);
                nightsOverwritten++;
            }
            else
            {
                nightsInserted++;
            }
        }

        // Vi skriver alla rader för nights som ska insert:as eller överskrivas.
        // Nights som redan finns och som vi inte överskriver → hoppas helt över.
        var nightsToWrite = fileNightNumbers
            .Where(n => !existingNightNumbers.Contains(n) || overwrite)
            .ToHashSet();

        var aggregatesToInsert = parsed.NightAggregates
            .Where(a => nightsToWrite.Contains(a.NightNumber))
            .ToList();

        var placementsToInsert = parsed.RoundPlacements
            .Where(p => nightsToWrite.Contains(p.NightNumber))
            .ToList();

        return new HistoricalImportPlan(
            NightNumbersToReplace: toReplace,
            AggregatesToInsert: aggregatesToInsert,
            PlacementsToInsert: placementsToInsert,
            NightsInserted: nightsInserted,
            NightsOverwritten: nightsOverwritten,
            NightsSkipped: nightsSkipped);
    }
}

public sealed record HistoricalImportPlan(
    IReadOnlyList<int> NightNumbersToReplace,
    IReadOnlyList<HistoricalNightAggregate> AggregatesToInsert,
    IReadOnlyList<HistoricalRoundPlacement> PlacementsToInsert,
    int NightsInserted,
    int NightsOverwritten,
    int NightsSkipped);
