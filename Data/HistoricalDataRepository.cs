using DoubleDashScore.Models;
using DoubleDashScore.Services;

namespace DoubleDashScore.Data;

public class HistoricalDataRepository
{
    private readonly DatabaseService _db;

    public HistoricalDataRepository(DatabaseService db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<HistoricalNightAggregate>> GetAllNightAggregatesAsync(CancellationToken ct = default)
    {
        var conn = await _db.GetConnectionAsync(ct).ConfigureAwait(false);
        return await conn.Table<HistoricalNightAggregate>().ToListAsync().ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<HistoricalRoundPlacement>> GetAllPlacementsAsync(CancellationToken ct = default)
    {
        var conn = await _db.GetConnectionAsync(ct).ConfigureAwait(false);
        return await conn.Table<HistoricalRoundPlacement>().ToListAsync().ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<HistoricalPositionTotalsSnapshot>> GetSnapshotAsync(CancellationToken ct = default)
    {
        var conn = await _db.GetConnectionAsync(ct).ConfigureAwait(false);
        return await conn.Table<HistoricalPositionTotalsSnapshot>().ToListAsync().ConfigureAwait(false);
    }

    public async Task<HistoricalSeed> GetSeedAsync(CancellationToken ct = default)
    {
        var aggregates = await GetAllNightAggregatesAsync(ct).ConfigureAwait(false);
        var placements = await GetAllPlacementsAsync(ct).ConfigureAwait(false);
        var snapshot = await GetSnapshotAsync(ct).ConfigureAwait(false);
        return new HistoricalSeed(aggregates, placements, snapshot);
    }

    public async Task<ImportPreview> ComputePreviewAsync(ParsedExcelImport parsed, CancellationToken ct = default)
    {
        var conn = await _db.GetConnectionAsync(ct).ConfigureAwait(false);

        var existingNightNumbers = (await conn.Table<HistoricalNightAggregate>().ToListAsync().ConfigureAwait(false))
            .Select(a => a.NightNumber)
            .ToHashSet();
        var existingPlacements = (await conn.Table<HistoricalRoundPlacement>().ToListAsync().ConfigureAwait(false))
            .Select(p => (p.NightNumber, p.PlayerId, p.RoundIndex))
            .ToHashSet();

        var fileNightNumbers = parsed.NightAggregates
            .Select(a => a.NightNumber)
            .Distinct()
            .OrderBy(n => n)
            .ToList();

        var newNightCount = fileNightNumbers.Count(n => !existingNightNumbers.Contains(n));

        var newPlacementCount = parsed.RoundPlacements
            .Count(p => !existingPlacements.Contains((p.NightNumber, p.PlayerId, p.RoundIndex)));

        var missing = new List<int>();
        if (fileNightNumbers.Count > 0)
        {
            var min = fileNightNumbers[0];
            var max = fileNightNumbers[^1];
            var seen = fileNightNumbers.ToHashSet();
            for (int n = min; n <= max; n++)
            {
                if (!seen.Contains(n)) missing.Add(n);
            }
        }

        return new ImportPreview(
            TotalNightsInFile: fileNightNumbers.Count,
            NewNightsToImport: newNightCount,
            NewPlacementsToImport: newPlacementCount,
            MissingNightNumbersInFile: missing,
            SnapshotWillBeReplaced: parsed.PositionTotalsSnapshot.Count > 0,
            PlayerNamesInExcelColumnOrder: parsed.PlayerNamesInExcelColumnOrder);
    }

    public async Task<ImportResult> ImportAsync(
        ParsedExcelImport parsed,
        bool overwrite = false,
        CancellationToken ct = default)
    {
        var conn = await _db.GetConnectionAsync(ct).ConfigureAwait(false);
        var now = DateTime.UtcNow;

        int nightsInserted = 0;
        int nightsOverwritten = 0;
        int placementsInserted = 0;
        bool snapshotReplaced = false;

        await conn.RunInTransactionAsync(tx =>
        {
            var existingNightNumbers = tx.Table<HistoricalNightAggregate>()
                .ToList()
                .Select(a => a.NightNumber)
                .ToHashSet();

            var plan = HistoricalImportPlanner.Plan(parsed, existingNightNumbers, overwrite);

            // Snapshot ersätts alltid (oavsett flagga) — det är hela tabellens semantik.
            // Med InsertOrReplace per PlayerId behåller vi rader för spelare som inte
            // står i filen, men det är ingen vanlig situation.
            foreach (var snap in parsed.PositionTotalsSnapshot)
            {
                snap.CreatedAt = now;
                tx.InsertOrReplace(snap);
                snapshotReplaced = true;
            }

            // Replace: radera alla rader för kvällar som skrivs över. Görs först så
            // att efterföljande Insert inte krockar med befintliga PK:er.
            foreach (var nightNumber in plan.NightNumbersToReplace)
            {
                tx.Execute(
                    "DELETE FROM HistoricalNightAggregates WHERE NightNumber = ?",
                    nightNumber);
                tx.Execute(
                    "DELETE FROM HistoricalRoundPlacements WHERE NightNumber = ?",
                    nightNumber);
            }

            foreach (var agg in plan.AggregatesToInsert)
            {
                agg.CreatedAt = now;
                tx.Insert(agg);
            }
            foreach (var placement in plan.PlacementsToInsert)
            {
                placement.CreatedAt = now;
                tx.Insert(placement);
            }

            nightsInserted = plan.NightsInserted;
            nightsOverwritten = plan.NightsOverwritten;
            placementsInserted = plan.PlacementsToInsert.Count;
        }).ConfigureAwait(false);

        return new ImportResult(
            nightsInserted,
            placementsInserted,
            snapshotReplaced,
            nightsOverwritten);
    }
}
