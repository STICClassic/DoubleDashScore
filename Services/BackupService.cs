namespace DoubleDashScore.Services;

// Rullande auto-backup av SQLite-databasen. Repositories anropar RequestBackup()
// efter varje committad write — calls debounce:as 2 sekunder så att burst:ar
// (en spara-transaktion kan trigga flera repo-anrop) coalesce:as till en
// faktisk filkopia. Behåller MaxBackups senaste, evictar äldsta.
//
// Backupen är en plain File.Copy av databasfilen. SQLite default journal_mode
// är DELETE (rollback journal) — efter committad transaktion är .db-filen
// komplett, ingen WAL-checkpoint behövs.
//
// Filer namnges backup-yyyy-MM-dd-HHmmss.db i lokal tid. Den lexikografiska
// ordningen sammanfaller med tidsordningen, så pruning kan sortera per
// filnamn utan att läsa FS-timestamps.
//
// Backup är säkerhetsåtgärd, inte kritisk path. Fel sväljs tyst — vi vill
// aldrig att en misslyckad backup ska krascha en write som lyckats.
public sealed class BackupService
{
    public const int MaxBackups = 10;
    public static readonly TimeSpan DefaultDebounceDelay = TimeSpan.FromSeconds(2);
    public const string BackupFilePrefix = "backup-";
    public const string BackupFileExtension = ".db";

    private readonly string _databasePath;
    private readonly string _backupDirectory;
    private readonly TimeSpan _debounceDelay;

    private readonly object _lock = new();
    private CancellationTokenSource? _pendingCts;

    public BackupService(string databasePath, string backupDirectory)
        : this(databasePath, backupDirectory, DefaultDebounceDelay)
    {
    }

    // Internt overload för tester som vill köra utan delay.
    internal BackupService(string databasePath, string backupDirectory, TimeSpan debounceDelay)
    {
        _databasePath = databasePath;
        _backupDirectory = backupDirectory;
        _debounceDelay = debounceDelay;
    }

    public string BackupDirectory => _backupDirectory;

    // Fire-and-forget. Trygg att kalla från en hot-path. Återstartar pending-
    // timern om det redan finns en — burst:ar inom debounceDelay coalesce:as.
    public void RequestBackup()
    {
        CancellationToken token;
        lock (_lock)
        {
            _pendingCts?.Cancel();
            _pendingCts?.Dispose();
            _pendingCts = new CancellationTokenSource();
            token = _pendingCts.Token;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(_debounceDelay, token).ConfigureAwait(false);
                await RunBackupNowAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Förnyad RequestBackup avbröt — den senare turnen gör backupen.
            }
            catch
            {
                // Tyst sväljning — backup är best-effort.
            }
        });
    }

    // Kör en backup omedelbart utan debounce. Tester använder denna; även
    // framtida "ta backup nu"-knapp i settings kan kalla den.
    public async Task RunBackupNowAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_databasePath))
        {
            return;
        }

        Directory.CreateDirectory(_backupDirectory);

        var fileName = $"{BackupFilePrefix}{DateTime.Now:yyyy-MM-dd-HHmmss}{BackupFileExtension}";
        var destPath = Path.Combine(_backupDirectory, fileName);

        // Om två backups hamnar på samma sekund (osannolikt, men möjligt vid
        // tester eller snabba programmatiska anrop) → append ett räknarsuffix
        // så vi inte överskriver föregående backup.
        if (File.Exists(destPath))
        {
            int counter = 1;
            string candidate;
            do
            {
                candidate = Path.Combine(
                    _backupDirectory,
                    $"{BackupFilePrefix}{DateTime.Now:yyyy-MM-dd-HHmmss}-{counter}{BackupFileExtension}");
                counter++;
            }
            while (File.Exists(candidate));
            destPath = candidate;
        }

        await using (var src = new FileStream(_databasePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        await using (var dst = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await src.CopyToAsync(dst, ct).ConfigureAwait(false);
        }

        PruneOldBackups(_backupDirectory, MaxBackups);
    }

    // Lista auto-backups sorterade nyast → äldst. Exponerad för framtida
    // "Återställ från auto-backup"-UI — vi behöver ingen återställning ännu
    // men API:et är gratis att exponera redan nu.
    public IReadOnlyList<string> ListAutoBackups()
    {
        if (!Directory.Exists(_backupDirectory))
        {
            return Array.Empty<string>();
        }
        return Directory.EnumerateFiles(_backupDirectory, $"{BackupFilePrefix}*{BackupFileExtension}")
            .OrderByDescending(Path.GetFileName, StringComparer.Ordinal)
            .ToList();
    }

    // Statisk så tester kan köra logiken mot en valfri temp-mapp utan att
    // instantiera BackupService eller röra MAUI:s FileSystem.
    internal static int PruneOldBackups(string directory, int keep)
    {
        if (!Directory.Exists(directory))
        {
            return 0;
        }

        // Lexikografisk sortering DESC på filnamn = tidsordning DESC eftersom
        // formatet är backup-yyyy-MM-dd-HHmmss.db. Skip(keep) lämnar de
        // `keep` nyaste, resten raderas.
        var toDelete = Directory.EnumerateFiles(directory, $"{BackupFilePrefix}*{BackupFileExtension}")
            .OrderByDescending(Path.GetFileName, StringComparer.Ordinal)
            .Skip(keep)
            .ToList();

        int deleted = 0;
        foreach (var path in toDelete)
        {
            try
            {
                File.Delete(path);
                deleted++;
            }
            catch
            {
                // Strunta i enskilda delete-fel; nästa pruning städar om.
            }
        }
        return deleted;
    }
}
