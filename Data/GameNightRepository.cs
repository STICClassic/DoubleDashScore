using DoubleDashScore.Models;

namespace DoubleDashScore.Data;

public record GameNightSummary(GameNight Night, int RoundCount, int CompleteRoundCount);

public class GameNightRepository
{
    private readonly DatabaseService _db;

    public GameNightRepository(DatabaseService db)
    {
        _db = db;
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

        var resultCountsByRound = await GetResultCountsByRoundAsync(conn).ConfigureAwait(false);

        var roundsByNight = rounds.GroupBy(r => r.GameNightId)
            .ToDictionary(g => g.Key, g => g.ToList());

        return nights.Select(n =>
        {
            roundsByNight.TryGetValue(n.Id, out var nightRounds);
            nightRounds ??= new List<Round>();
            var complete = nightRounds.Count(r =>
                r.TrackCount == 16 &&
                resultCountsByRound.TryGetValue(r.Id, out var c) &&
                c == 4);
            return new GameNightSummary(n, nightRounds.Count, complete);
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
    }

    private static async Task<Dictionary<int, int>> GetResultCountsByRoundAsync(SQLite.SQLiteAsyncConnection conn)
    {
        var rows = await conn.QueryAsync<RoundResultCountRow>(
            "SELECT RoundId, COUNT(*) AS Count FROM RoundResults WHERE DeletedAt IS NULL GROUP BY RoundId")
            .ConfigureAwait(false);
        return rows.ToDictionary(r => r.RoundId, r => r.Count);
    }

    private sealed class RoundResultCountRow
    {
        public int RoundId { get; set; }
        public int Count { get; set; }
    }
}
