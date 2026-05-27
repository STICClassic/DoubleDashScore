using System.Globalization;
using DoubleDashScore.Services;
using Xunit;

namespace DoubleDashScore.Tests;

public class BackupFileNamingTests
{
    private static readonly CultureInfo SvSe = CultureInfo.GetCultureInfo("sv-SE");

    [Fact]
    public void TryParseTimestamp_StandardName_ReturnsTrue()
    {
        var ok = BackupFileNaming.TryParseTimestamp("backup-2026-05-18-143200.db", out var ts);

        Assert.True(ok);
        Assert.Equal(new DateTime(2026, 5, 18, 14, 32, 0), ts);
    }

    [Fact]
    public void TryParseTimestamp_FullPath_AcceptsPath()
    {
        var ok = BackupFileNaming.TryParseTimestamp(
            "/data/data/com.app/files/auto-backups/backup-2026-05-18-143200.db",
            out var ts);

        Assert.True(ok);
        Assert.Equal(new DateTime(2026, 5, 18, 14, 32, 0), ts);
    }

    [Fact]
    public void TryParseTimestamp_WithCollisionSuffix_StillParsesBaseTimestamp()
    {
        var ok = BackupFileNaming.TryParseTimestamp("backup-2026-05-18-143200-1.db", out var ts);

        Assert.True(ok);
        Assert.Equal(new DateTime(2026, 5, 18, 14, 32, 0), ts);
    }

    [Fact]
    public void TryParseTimestamp_NonBackupFile_ReturnsFalse()
    {
        Assert.False(BackupFileNaming.TryParseTimestamp("notes.txt", out _));
        Assert.False(BackupFileNaming.TryParseTimestamp("doubledashscore.db3", out _));
        Assert.False(BackupFileNaming.TryParseTimestamp("backup-notatimestamp.db", out _));
        Assert.False(BackupFileNaming.TryParseTimestamp(string.Empty, out _));
    }

    [Fact]
    public void FormatTimestamp_StandardName_ReturnsSwedishLabel()
    {
        var label = BackupFileNaming.FormatTimestamp("backup-2026-05-18-143200.db", SvSe);

        // "d MMMM yyyy, HH:mm" på sv-SE → t.ex. "18 maj 2026, 14:32".
        Assert.Equal("18 maj 2026, 14:32", label);
    }

    [Fact]
    public void FormatTimestamp_UnparsableName_FallsBackToFileName()
    {
        var label = BackupFileNaming.FormatTimestamp("/some/path/garbage.db", SvSe);

        Assert.Equal("garbage.db", label);
    }
}
