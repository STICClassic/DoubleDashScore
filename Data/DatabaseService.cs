using DoubleDashScore.Models;
using SQLite;

namespace DoubleDashScore.Data;

public class DatabaseService
{
    private const string DatabaseFileName = "doubledashscore.db3";

    private static readonly string DatabasePath =
        Path.Combine(FileSystem.AppDataDirectory, DatabaseFileName);

    private SQLiteAsyncConnection? _connection;
    private bool _initialized;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    public async Task<SQLiteAsyncConnection> GetConnectionAsync(CancellationToken ct = default)
    {
        if (_initialized && _connection is not null)
        {
            return _connection;
        }

        await _initLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_initialized && _connection is not null)
            {
                return _connection;
            }

            _connection = new SQLiteAsyncConnection(
                DatabasePath,
                SQLiteOpenFlags.Create | SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.SharedCache);

            await _connection.CreateTableAsync<Player>().ConfigureAwait(false);
            await _connection.CreateTableAsync<GameNight>().ConfigureAwait(false);
            await _connection.CreateTableAsync<Round>().ConfigureAwait(false);
            await _connection.CreateTableAsync<RoundResult>().ConfigureAwait(false);
            await _connection.CreateTableAsync<HistoricalNightAggregate>().ConfigureAwait(false);
            await _connection.CreateTableAsync<HistoricalRoundPlacement>().ConfigureAwait(false);
            await _connection.CreateTableAsync<HistoricalPositionTotalsSnapshot>().ConfigureAwait(false);

            await SeedDefaultPlayersAsync(_connection).ConfigureAwait(false);

            _initialized = true;
            return _connection;
        }
        finally
        {
            _initLock.Release();
        }
    }

    private static async Task SeedDefaultPlayersAsync(SQLiteAsyncConnection conn)
    {
        var existing = await conn.Table<Player>().CountAsync().ConfigureAwait(false);
        if (existing > 0)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var defaults = Enumerable.Range(1, 4).Select(i => new Player
        {
            Name = $"Spelare {i}",
            DisplayOrder = i - 1,
            CreatedAt = now,
        }).ToList();

        await conn.InsertAllAsync(defaults).ConfigureAwait(false);
    }
}
