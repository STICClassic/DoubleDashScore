using DoubleDashScore.Services;
using Xunit;

namespace DoubleDashScore.Tests;

public class BackupServiceTests : IDisposable
{
    private readonly string _tempDir;

    public BackupServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ddscore-backup-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void PruneOldBackups_With12Files_Keeps10Newest()
    {
        // Skapa 12 backup-filer med stigande timestamps. Lexikografisk ordning
        // sammanfaller med tidsordning eftersom format är yyyy-MM-dd-HHmmss.
        var fileNames = new List<string>();
        for (int i = 0; i < 12; i++)
        {
            var name = $"backup-2026-01-{(i + 1):D2}-120000.db";
            fileNames.Add(name);
            File.WriteAllText(Path.Combine(_tempDir, name), $"content-{i}");
        }

        var deleted = BackupService.PruneOldBackups(_tempDir, keep: 10);

        Assert.Equal(2, deleted);
        var remaining = Directory.GetFiles(_tempDir, "backup-*.db")
            .Select(Path.GetFileName)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();
        Assert.Equal(10, remaining.Count);
        // De äldsta två (jan 1 och jan 2) ska vara borta.
        Assert.DoesNotContain("backup-2026-01-01-120000.db", remaining);
        Assert.DoesNotContain("backup-2026-01-02-120000.db", remaining);
        Assert.Contains("backup-2026-01-03-120000.db", remaining);
        Assert.Contains("backup-2026-01-12-120000.db", remaining);
    }

    [Fact]
    public void PruneOldBackups_With9Files_KeepsAll()
    {
        for (int i = 0; i < 9; i++)
        {
            File.WriteAllText(
                Path.Combine(_tempDir, $"backup-2026-01-{(i + 1):D2}-120000.db"),
                $"content-{i}");
        }

        var deleted = BackupService.PruneOldBackups(_tempDir, keep: 10);

        Assert.Equal(0, deleted);
        Assert.Equal(9, Directory.GetFiles(_tempDir, "backup-*.db").Length);
    }

    [Fact]
    public void PruneOldBackups_Exactly10Files_NoDelete()
    {
        for (int i = 0; i < 10; i++)
        {
            File.WriteAllText(
                Path.Combine(_tempDir, $"backup-2026-01-{(i + 1):D2}-120000.db"),
                $"content-{i}");
        }

        var deleted = BackupService.PruneOldBackups(_tempDir, keep: 10);

        Assert.Equal(0, deleted);
        Assert.Equal(10, Directory.GetFiles(_tempDir, "backup-*.db").Length);
    }

    [Fact]
    public void PruneOldBackups_IgnoresFilesNotMatchingPattern()
    {
        // 11 riktiga backups + 3 ovidkommande filer. Endast den äldsta backupen
        // ska raderas; de övriga filerna får ligga kvar.
        for (int i = 0; i < 11; i++)
        {
            File.WriteAllText(
                Path.Combine(_tempDir, $"backup-2026-01-{(i + 1):D2}-120000.db"),
                "x");
        }
        File.WriteAllText(Path.Combine(_tempDir, "notes.txt"), "x");
        File.WriteAllText(Path.Combine(_tempDir, "doubledashscore.db3"), "x");
        File.WriteAllText(Path.Combine(_tempDir, "random-2026-01-99-120000.db"), "x");

        var deleted = BackupService.PruneOldBackups(_tempDir, keep: 10);

        Assert.Equal(1, deleted);
        Assert.False(File.Exists(Path.Combine(_tempDir, "backup-2026-01-01-120000.db")));
        Assert.True(File.Exists(Path.Combine(_tempDir, "notes.txt")));
        Assert.True(File.Exists(Path.Combine(_tempDir, "doubledashscore.db3")));
        Assert.True(File.Exists(Path.Combine(_tempDir, "random-2026-01-99-120000.db")));
    }

    [Fact]
    public void PruneOldBackups_NonExistentDirectory_ReturnsZero()
    {
        var missing = Path.Combine(_tempDir, "does-not-exist");

        var deleted = BackupService.PruneOldBackups(missing, keep: 10);

        Assert.Equal(0, deleted);
    }

    [Fact]
    public async Task RunBackupNowAsync_CopiesDatabaseFileToBackupDirectory()
    {
        var dbPath = Path.Combine(_tempDir, "source.db3");
        var backupDir = Path.Combine(_tempDir, "auto-backups");
        File.WriteAllText(dbPath, "fake-sqlite-content");

        var service = new BackupService(dbPath, backupDir, TimeSpan.Zero);
        await service.RunBackupNowAsync();

        var backups = Directory.GetFiles(backupDir, "backup-*.db");
        Assert.Single(backups);
        Assert.Equal("fake-sqlite-content", File.ReadAllText(backups[0]));
    }

    [Fact]
    public async Task RunBackupNowAsync_ProducesDotDbExtension()
    {
        var dbPath = Path.Combine(_tempDir, "source.db3");
        var backupDir = Path.Combine(_tempDir, "auto-backups");
        File.WriteAllText(dbPath, "content");

        var service = new BackupService(dbPath, backupDir, TimeSpan.Zero);
        await service.RunBackupNowAsync();

        var backup = Directory.GetFiles(backupDir).Single();
        Assert.EndsWith(".db", backup);
        Assert.DoesNotContain(".db3", Path.GetFileName(backup));
    }

    [Fact]
    public async Task RunBackupNowAsync_PrunesAfterReachingMaxBackups()
    {
        var dbPath = Path.Combine(_tempDir, "source.db3");
        var backupDir = Path.Combine(_tempDir, "auto-backups");
        File.WriteAllText(dbPath, "content");
        Directory.CreateDirectory(backupDir);
        // 10 redan existerande backups med äldre namn — efter en ny ska de
        // äldsta evictas så vi fortfarande har max 10.
        for (int i = 0; i < BackupService.MaxBackups; i++)
        {
            File.WriteAllText(
                Path.Combine(backupDir, $"backup-2020-01-{(i + 1):D2}-120000.db"),
                $"old-{i}");
        }

        var service = new BackupService(dbPath, backupDir, TimeSpan.Zero);
        await service.RunBackupNowAsync();

        Assert.Equal(BackupService.MaxBackups, Directory.GetFiles(backupDir, "backup-*.db").Length);
        // Den äldsta från 2020-01-01 ska vara borta; nya från 2026+ ska finnas.
        Assert.False(File.Exists(Path.Combine(backupDir, "backup-2020-01-01-120000.db")));
    }

    [Fact]
    public async Task ListAutoBackups_ReturnsBackupsSortedNewestFirst()
    {
        var dbPath = Path.Combine(_tempDir, "source.db3");
        var backupDir = Path.Combine(_tempDir, "auto-backups");
        Directory.CreateDirectory(backupDir);
        File.WriteAllText(Path.Combine(backupDir, "backup-2026-01-05-120000.db"), "x");
        File.WriteAllText(Path.Combine(backupDir, "backup-2026-01-01-120000.db"), "x");
        File.WriteAllText(Path.Combine(backupDir, "backup-2026-01-10-120000.db"), "x");

        var service = new BackupService(dbPath, backupDir, TimeSpan.Zero);
        var list = service.ListAutoBackups();

        Assert.Equal(3, list.Count);
        Assert.EndsWith("backup-2026-01-10-120000.db", list[0]);
        Assert.EndsWith("backup-2026-01-05-120000.db", list[1]);
        Assert.EndsWith("backup-2026-01-01-120000.db", list[2]);

        // Bekräfta att RunBackupNowAsync inte krävdes ovan — vi vill ha ett
        // explicit no-op-test för funktionen mot icke-existerande mapp.
        await Task.CompletedTask;
    }
}
