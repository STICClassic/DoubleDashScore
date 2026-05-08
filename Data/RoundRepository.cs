using DoubleDashScore.Models;
using SQLite;

namespace DoubleDashScore.Data;

public sealed record RoundResultInput(
    int PlayerId,
    int FirstPlaces,
    int SecondPlaces,
    int ThirdPlaces,
    int FourthPlaces);

public sealed record RoundDetail(Round Round, IReadOnlyList<RoundResult> Results)
{
    public bool IsComplete => Round.TrackCount == 16 && Results.Count == 4;
}

public class RoundRepository
{
    private readonly DatabaseService _db;

    public RoundRepository(DatabaseService db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<RoundDetail>> GetRoundsForNightAsync(int gameNightId, CancellationToken ct = default)
    {
        var conn = await _db.GetConnectionAsync(ct).ConfigureAwait(false);
        var rounds = await conn.Table<Round>()
            .Where(r => r.GameNightId == gameNightId && r.DeletedAt == null)
            .OrderBy(r => r.RoundNumber)
            .ToListAsync()
            .ConfigureAwait(false);

        if (rounds.Count == 0)
        {
            return Array.Empty<RoundDetail>();
        }

        var roundIds = rounds.Select(r => r.Id).ToHashSet();
        var allResults = await conn.Table<RoundResult>()
            .Where(rr => rr.DeletedAt == null)
            .ToListAsync()
            .ConfigureAwait(false);

        var resultsByRound = allResults
            .Where(rr => roundIds.Contains(rr.RoundId))
            .GroupBy(rr => rr.RoundId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<RoundResult>)g.ToList());

        return rounds.Select(r =>
        {
            resultsByRound.TryGetValue(r.Id, out var results);
            return new RoundDetail(r, results ?? Array.Empty<RoundResult>());
        }).ToList();
    }

    public async Task<RoundDetail?> GetRoundAsync(int roundId, CancellationToken ct = default)
    {
        var conn = await _db.GetConnectionAsync(ct).ConfigureAwait(false);
        var round = await conn.Table<Round>()
            .Where(r => r.Id == roundId && r.DeletedAt == null)
            .FirstOrDefaultAsync()
            .ConfigureAwait(false);
        if (round is null)
        {
            return null;
        }
        var results = await conn.Table<RoundResult>()
            .Where(rr => rr.RoundId == roundId && rr.DeletedAt == null)
            .ToListAsync()
            .ConfigureAwait(false);
        return new RoundDetail(round, results);
    }

    public async Task<int> CreateRoundAsync(
        int gameNightId,
        int trackCount,
        IReadOnlyList<RoundResultInput> results,
        CancellationToken ct = default)
    {
        ValidateResults(trackCount, results);

        var conn = await _db.GetConnectionAsync(ct).ConfigureAwait(false);
        var nextRoundNumber = await NextRoundNumberAsync(conn, gameNightId).ConfigureAwait(false);
        var now = DateTime.UtcNow;
        int newRoundId = 0;

        await conn.RunInTransactionAsync(tx =>
        {
            var round = new Round
            {
                GameNightId = gameNightId,
                RoundNumber = nextRoundNumber,
                TrackCount = trackCount,
                CreatedAt = now,
            };
            tx.Insert(round);
            newRoundId = round.Id;

            foreach (var r in results)
            {
                tx.Insert(new RoundResult
                {
                    RoundId = round.Id,
                    PlayerId = r.PlayerId,
                    FirstPlaces = r.FirstPlaces,
                    SecondPlaces = r.SecondPlaces,
                    ThirdPlaces = r.ThirdPlaces,
                    FourthPlaces = r.FourthPlaces,
                    CreatedAt = now,
                });
            }
        }).ConfigureAwait(false);

        return newRoundId;
    }

    public async Task UpdateRoundAsync(
        int roundId,
        int trackCount,
        IReadOnlyList<RoundResultInput> results,
        CancellationToken ct = default)
    {
        ValidateResults(trackCount, results);

        var conn = await _db.GetConnectionAsync(ct).ConfigureAwait(false);
        var now = DateTime.UtcNow;

        await conn.RunInTransactionAsync(tx =>
        {
            var round = tx.Find<Round>(roundId)
                ?? throw new InvalidOperationException($"Round {roundId} not found.");
            if (round.DeletedAt is not null)
            {
                throw new InvalidOperationException($"Round {roundId} is deleted.");
            }
            round.TrackCount = trackCount;
            tx.Update(round);

            var existing = tx.Table<RoundResult>()
                .Where(rr => rr.RoundId == roundId && rr.DeletedAt == null)
                .ToList();
            foreach (var rr in existing)
            {
                rr.DeletedAt = now;
                tx.Update(rr);
            }

            foreach (var r in results)
            {
                tx.Insert(new RoundResult
                {
                    RoundId = roundId,
                    PlayerId = r.PlayerId,
                    FirstPlaces = r.FirstPlaces,
                    SecondPlaces = r.SecondPlaces,
                    ThirdPlaces = r.ThirdPlaces,
                    FourthPlaces = r.FourthPlaces,
                    CreatedAt = now,
                });
            }
        }).ConfigureAwait(false);
    }

    public async Task SoftDeleteRoundAsync(int roundId, CancellationToken ct = default)
    {
        var conn = await _db.GetConnectionAsync(ct).ConfigureAwait(false);
        var now = DateTime.UtcNow;
        await conn.RunInTransactionAsync(tx =>
        {
            var round = tx.Find<Round>(roundId);
            if (round is null || round.DeletedAt is not null)
            {
                return;
            }
            round.DeletedAt = now;
            tx.Update(round);

            var results = tx.Table<RoundResult>()
                .Where(rr => rr.RoundId == roundId && rr.DeletedAt == null)
                .ToList();
            foreach (var rr in results)
            {
                rr.DeletedAt = now;
                tx.Update(rr);
            }
        }).ConfigureAwait(false);
    }

    public async Task<RoundDetail?> GetMostRecentRoundForNightAsync(int gameNightId, CancellationToken ct = default)
    {
        var conn = await _db.GetConnectionAsync(ct).ConfigureAwait(false);
        var round = await conn.Table<Round>()
            .Where(r => r.GameNightId == gameNightId && r.DeletedAt == null)
            .OrderByDescending(r => r.RoundNumber)
            .FirstOrDefaultAsync()
            .ConfigureAwait(false);
        if (round is null)
        {
            return null;
        }
        var results = await conn.Table<RoundResult>()
            .Where(rr => rr.RoundId == round.Id && rr.DeletedAt == null)
            .ToListAsync()
            .ConfigureAwait(false);
        return new RoundDetail(round, results);
    }

    private static async Task<int> NextRoundNumberAsync(SQLiteAsyncConnection conn, int gameNightId)
    {
        var existing = await conn.Table<Round>()
            .Where(r => r.GameNightId == gameNightId && r.DeletedAt == null)
            .ToListAsync()
            .ConfigureAwait(false);
        return existing.Count == 0 ? 1 : existing.Max(r => r.RoundNumber) + 1;
    }

    internal static void ValidateResults(int trackCount, IReadOnlyList<RoundResultInput> results)
    {
        if (trackCount < 1 || trackCount > 16)
        {
            throw new ArgumentException("TrackCount must be between 1 and 16.", nameof(trackCount));
        }
        if (results.Count != 4)
        {
            throw new ArgumentException("Exactly 4 player results are required.", nameof(results));
        }
        var distinctPlayers = results.Select(r => r.PlayerId).Distinct().Count();
        if (distinctPlayers != 4)
        {
            throw new ArgumentException("Each player must appear exactly once.", nameof(results));
        }
        foreach (var r in results)
        {
            if (r.FirstPlaces < 0 || r.SecondPlaces < 0 || r.ThirdPlaces < 0 || r.FourthPlaces < 0)
            {
                throw new ArgumentException("Counts cannot be negative.", nameof(results));
            }
            var sum = r.FirstPlaces + r.SecondPlaces + r.ThirdPlaces + r.FourthPlaces;
            if (sum != trackCount)
            {
                throw new ArgumentException(
                    $"Player {r.PlayerId}: counts sum to {sum}, expected {trackCount}.",
                    nameof(results));
            }
        }
        var firstSum = results.Sum(r => r.FirstPlaces);
        var secondSum = results.Sum(r => r.SecondPlaces);
        var thirdSum = results.Sum(r => r.ThirdPlaces);
        var fourthSum = results.Sum(r => r.FourthPlaces);
        if (firstSum != trackCount || secondSum != trackCount || thirdSum != trackCount || fourthSum != trackCount)
        {
            throw new ArgumentException(
                $"Per-position sums must each equal {trackCount}: 1st={firstSum}, 2nd={secondSum}, 3rd={thirdSum}, 4th={fourthSum}.",
                nameof(results));
        }
    }
}
