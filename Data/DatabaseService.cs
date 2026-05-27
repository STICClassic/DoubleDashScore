using DoubleDashScore.Models;
using SQLite;

namespace DoubleDashScore.Data;

public class DatabaseService
{
    private const string DatabaseFileName = "doubledashscore.db3";

    public static readonly string DatabasePath =
        Path.Combine(FileSystem.AppDataDirectory, DatabaseFileName);

    // Tabeller en giltig DoubleDashScore-backup MÅSTE innehålla.
    // Används av ReplaceDatabaseAsync för att avvisa felaktiga filer innan
    // vi rör vid den aktiva databasfilen.
    private static readonly string[] RequiredTables = new[]
    {
        "Players",
        "GameNights",
        "Rounds",
        "RoundResults",
        "HistoricalNightAggregates",
        "HistoricalRoundPlacements",
        "HistoricalPositionTotalsSnapshot",
    };

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

    // Ersätter den aktiva DB-filen med innehållet i sourcePath. Validerar källfilen
    // som SQLite + förväntade tabeller INNAN något skrivs. Backupar nuvarande fil
    // till temp och rullar tillbaka om swap eller efterföljande öppning failar.
    // Återställer _initialized=false så att nästa GetConnectionAsync återinitierar
    // anslutningen mot den nya filen (inklusive CreateTableAsync för schema-migrering).
    public async Task ReplaceDatabaseAsync(string sourcePath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            throw new ArgumentException("Källsökväg saknas.", nameof(sourcePath));
        }
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("Filen hittades inte.", sourcePath);
        }

        await ValidateDatabaseFileAsync(sourcePath, ct).ConfigureAwait(false);

        await _initLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Släpp aktivt fil-handle innan vi skriver över filen.
            if (_connection is not null)
            {
                await _connection.CloseAsync().ConfigureAwait(false);
                _connection = null;
            }
            _initialized = false;

            string? backupPath = null;
            if (File.Exists(DatabasePath))
            {
                backupPath = DatabasePath + ".import-backup";
                File.Copy(DatabasePath, backupPath, overwrite: true);
            }

            try
            {
                File.Copy(sourcePath, DatabasePath, overwrite: true);

                // Smoke-test: öppna nya filen och kör CreateTableAsync för varje
                // entitet. CreateTableAsync är idempotent — för en valid backup är
                // det en no-op, för en backup med äldre schema lägger den till
                // saknade kolumner. Failar verifieringen → rulla tillbaka.
                var verify = new SQLiteAsyncConnection(
                    DatabasePath,
                    SQLiteOpenFlags.Create | SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.SharedCache);
                try
                {
                    await verify.CreateTableAsync<Player>().ConfigureAwait(false);
                    await verify.CreateTableAsync<GameNight>().ConfigureAwait(false);
                    await verify.CreateTableAsync<Round>().ConfigureAwait(false);
                    await verify.CreateTableAsync<RoundResult>().ConfigureAwait(false);
                    await verify.CreateTableAsync<HistoricalNightAggregate>().ConfigureAwait(false);
                    await verify.CreateTableAsync<HistoricalRoundPlacement>().ConfigureAwait(false);
                    await verify.CreateTableAsync<HistoricalPositionTotalsSnapshot>().ConfigureAwait(false);
                }
                finally
                {
                    await verify.CloseAsync().ConfigureAwait(false);
                }
            }
            catch
            {
                if (backupPath is not null && File.Exists(backupPath))
                {
                    File.Copy(backupPath, DatabasePath, overwrite: true);
                }
                throw;
            }
            finally
            {
                if (backupPath is not null && File.Exists(backupPath))
                {
                    try { File.Delete(backupPath); }
                    catch { /* lämna kvar om delete misslyckas; nästa import städar */ }
                }
            }
        }
        finally
        {
            _initLock.Release();
        }
    }

    private static async Task ValidateDatabaseFileAsync(string sourcePath, CancellationToken ct)
    {
        SQLiteAsyncConnection? conn = null;
        try
        {
            // ReadOnly så vi inte råkar skapa en helt ny tom DB om sourcePath
            // skulle vara en konstig fil. SharedCache för att matcha hur appen
            // annars öppnar SQLite.
            conn = new SQLiteAsyncConnection(
                sourcePath,
                SQLiteOpenFlags.ReadOnly | SQLiteOpenFlags.SharedCache);

            var rows = await conn.QueryAsync<SqliteMasterRow>(
                "SELECT name FROM sqlite_master WHERE type = 'table'")
                .ConfigureAwait(false);
            var present = new HashSet<string>(
                rows.Select(r => r.Name ?? string.Empty),
                StringComparer.OrdinalIgnoreCase);

            var missing = RequiredTables.Where(t => !present.Contains(t)).ToList();
            if (missing.Count > 0)
            {
                throw new InvalidDataException(
                    "Filen ser inte ut att vara en DoubleDashScore-backup. " +
                    $"Saknade tabeller: {string.Join(", ", missing)}.");
            }
        }
        catch (SQLiteException ex)
        {
            throw new InvalidDataException(
                "Filen är inte en giltig SQLite-databas.", ex);
        }
        finally
        {
            if (conn is not null)
            {
                await conn.CloseAsync().ConfigureAwait(false);
            }
        }
    }

    private class SqliteMasterRow
    {
        [Column("name")]
        public string? Name { get; set; }
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
