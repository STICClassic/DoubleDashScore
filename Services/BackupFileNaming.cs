using System.Globalization;

namespace DoubleDashScore.Services;

// Parsar och formaterar tidsstämpeln i auto-backup-filnamn. Format:
// backup-yyyy-MM-dd-HHmmss.db. Used av RestoreAutoBackupViewModel för att
// visa en läsbar etikett ("18 maj 2026, 14:32") och av Inställningar för
// "Senaste auto-backup"-raden.
public static class BackupFileNaming
{
    private const string TimestampFormat = "yyyy-MM-dd-HHmmss";

    public static bool TryParseTimestamp(string fileNameOrPath, out DateTime timestamp)
    {
        timestamp = default;
        if (string.IsNullOrWhiteSpace(fileNameOrPath))
        {
            return false;
        }

        var name = Path.GetFileName(fileNameOrPath);
        if (!name.StartsWith(BackupService.BackupFilePrefix, StringComparison.Ordinal) ||
            !name.EndsWith(BackupService.BackupFileExtension, StringComparison.Ordinal))
        {
            return false;
        }

        var core = name.Substring(
            BackupService.BackupFilePrefix.Length,
            name.Length - BackupService.BackupFilePrefix.Length - BackupService.BackupFileExtension.Length);

        // Suffix för krockade tidsstämplar ("...-HHmmss-1.db") — strippa allt
        // efter det 15:e tecknet om sådant finns. Standardfallet är exakt 15
        // tecken: 4+1+2+1+2+1+6.
        if (core.Length > TimestampFormat.Length)
        {
            core = core.Substring(0, TimestampFormat.Length);
        }

        return DateTime.TryParseExact(
            core,
            TimestampFormat,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeLocal,
            out timestamp);
    }

    // Returnerar en läsbar svensk tidsstämpel ("18 maj 2026, 14:32") om
    // filnamnet kan parsas, annars filnamnet självt som fallback.
    public static string FormatTimestamp(string fileNameOrPath, CultureInfo culture)
    {
        if (TryParseTimestamp(fileNameOrPath, out var ts))
        {
            return ts.ToString("d MMMM yyyy, HH:mm", culture);
        }
        return Path.GetFileName(fileNameOrPath);
    }
}
