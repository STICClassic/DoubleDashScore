using DoubleDashScore.Models;
using DoubleDashScore.Services;

namespace DoubleDashScore.Data;

// WinnersByRound: vinnar-spelarnas Id per komplett omgång, i kronologisk
// omgångsordning (RoundNumber stigande). En inre lista per omgång eftersom
// delad seger (tie) ger flera vinnare. Partiella omgångar utelämnas helt.
public record GameNightSummary(
    GameNight Night,
    int RoundCount,
    int CompleteRoundCount,
    IReadOnlyList<IReadOnlyList<int>> WinnersByRound);

public class GameNightRepository
{
    private readonly DatabaseService _db;
    private readonly BackupService _backup;

    public GameNightRepository(DatabaseService db, BackupService backup)
    {
        _db = db;
        _backup = backup;
    }

    public async Task<int> CreateAsync(DateTime playedOnUtc, string? note, CancellationToken ct = default)
    {
        var conn = await _db.GetConnectionAsync(ct).ConfigureAwait(false);
        var night = new GameNight
        {
            PlayedOn = playedOnUtc,
            Note = note,
            CreatedAt = DateTime.UtcNow,
        };
        await conn.InsertAsync(night).ConfigureAwait(false);
        _backup.RequestBackup();
        return night.Id;
    }

    public async Task<GameNight?> GetAsync(int id, CancellationToken ct = default)
    {
        var conn = await _db.GetConnectionAsync(ct).ConfigureAwait(false);
        return await conn.Table<GameNight>()
            .Where(n => n.Id == id && n.DeletedAt == null)
            .FirstOrDefaultAsync()
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<GameNightSummary>> GetSummariesAsync(CancellationToken ct = default)
    {
        var conn = await _db.GetConnectionAsync(ct).ConfigureAwait(false);
        var nights = await conn.Table<GameNight>()
            .Where(n => n.DeletedAt == null)
            .OrderByDescending(n => n.PlayedOn)
            .ToListAsync()
            .ConfigureAwait(false);

        if (nights.Count == 0)
        {
            return Array.Empty<GameNightSummary>();
        }

        var rounds = await conn.Table<Round>()
            .Where(r => r.DeletedAt == null)
            .ToListAsync()
            .ConfigureAwait(false);

        var roundIds = rounds.Select(r => r.Id).ToHashSet();
        var allResults = roundIds.Count == 0
            ? new List<RoundResult>()
            : await conn.Table<RoundResult>()
                .Where(rr => rr.DeletedAt == null)
                .ToListAsync()
                .ConfigureAwait(false);

        var resultsByRound = allResults
            .Where(rr => roundIds.Contains(rr.RoundId))
            .GroupBy(rr => rr.RoundId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var roundsByNight = rounds.GroupBy(r => r.GameNightId)
            .ToDictionary(g => g.Key, g => g.ToList());

        return nights.Select(n =>
        {
            roundsByNight.TryGetValue(n.Id, out var nightRounds);
            nightRounds ??= new List<Round>();

            // Komplett-villkoret avgör både antalsräkningen och vilka omgångar
            // som får en vinnare — samma loop. Regeln själv bor i
            // RoundCompletionRule (delas med RoundDetail.IsComplete) så villkoret
            // inte dupliceras.
            var complete = 0;
            var winnersByRound = new List<IReadOnlyList<int>>();
            foreach (var round in nightRounds.OrderBy(r => r.RoundNumber))
            {
                resultsByRound.TryGetValue(round.Id, out var results);
                results ??= new List<RoundResult>();
                if (!RoundCompletionRule.IsComplete(round.TrackCount, results.Count))
                {
                    continue;
                }
                complete++;

                // Vinnare = spelaren/spelarna med flest banpoäng i omgången.
                // StatsCalculator.PointsFor är källan till poängformeln (delas
                // med statistiken). Lika poäng ⇒ delad seger, flera vinnare.
                var maxPoints = results.Max(StatsCalculator.PointsFor);
                var winners = results
                    .Where(r => StatsCalculator.PointsFor(r) == maxPoints)
                    .Select(r => r.PlayerId)
                    .ToList();
                winnersByRound.Add(winners);
            }

            return new GameNightSummary(n, nightRounds.Count, complete, winnersByRound);
        }).ToList();
    }

    public async Task<IReadOnlyList<NightWithRounds>> GetAllNightsWithRoundsAsync(CancellationToken ct = default)
    {
        var conn = await _db.GetConnectionAsync(ct).ConfigureAwait(false);

        var nights = await conn.Table<GameNight>()
            .Where(n => n.DeletedAt == null)
            .OrderBy(n => n.PlayedOn)
            .ToListAsync()
            .ConfigureAwait(false);

        if (nights.Count == 0)
        {
            return Array.Empty<NightWithRounds>();
        }

        var rounds = await conn.Table<Round>()
            .Where(r => r.DeletedAt == null)
            .ToListAsync()
            .ConfigureAwait(false);

        var roundIds = rounds.Select(r => r.Id).ToHashSet();
        var allResults = roundIds.Count == 0
            ? new List<RoundResult>()
            : await conn.Table<RoundResult>()
                .Where(rr => rr.DeletedAt == null)
                .ToListAsync()
                .ConfigureAwait(false);

        var resultsByRound = allResults
            .Where(rr => roundIds.Contains(rr.RoundId))
            .GroupBy(rr => rr.RoundId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<RoundResult>)g.ToList());

        var roundsByNight = rounds
            .OrderBy(r => r.RoundNumber)
            .GroupBy(r => r.GameNightId)
            .ToDictionary(g => g.Key, g => g.ToList());

        return nights.Select(n =>
        {
            roundsByNight.TryGetValue(n.Id, out var nightRounds);
            var details = (nightRounds ?? new List<Round>())
                .Select(r =>
                {
                    resultsByRound.TryGetValue(r.Id, out var rs);
                    return new RoundDetail(r, rs ?? Array.Empty<RoundResult>());
                })
                .ToList();
            return new NightWithRounds(n, details);
        }).ToList();
    }

    // Uppdaterar enbart anteckningen (Note). Tom/whitespace ⇒ null så att
    // "anteckning saknas"-tillståndet blir konsekvent (placeholder visas).
    // Trimning sker hos anroparen. Ingen soft delete — vi UPDATE:ar fältet.
    public async Task UpdateNoteAsync(int id, string? note, CancellationToken ct = default)
    {
        var conn = await _db.GetConnectionAsync(ct).ConfigureAwait(false);
        var night = await conn.FindAsync<GameNight>(id).ConfigureAwait(false);
        if (night is null || night.DeletedAt is not null)
        {
            return;
        }
        night.Note = string.IsNullOrWhiteSpace(note) ? null : note;
        await conn.UpdateAsync(night).ConfigureAwait(false);
        _backup.RequestBackup();
    }

    public async Task SoftDeleteAsync(int id, CancellationToken ct = default)
    {
        var conn = await _db.GetConnectionAsync(ct).ConfigureAwait(false);
        var night = await conn.FindAsync<GameNight>(id).ConfigureAwait(false);
        if (night is null || night.DeletedAt is not null)
        {
            return;
        }
        night.DeletedAt = DateTime.UtcNow;
        await conn.UpdateAsync(night).ConfigureAwait(false);
        _backup.RequestBackup();
    }

}
