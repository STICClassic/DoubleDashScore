using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using DoubleDashScore.Data;
using DoubleDashScore.Services;

namespace DoubleDashScore.ViewModels;

public partial class RestoreAutoBackupViewModel : ObservableObject
{
    private static readonly CultureInfo SvSe = CultureInfo.GetCultureInfo("sv-SE");

    private readonly BackupService _backup;
    private readonly DatabaseService _database;

    public RestoreAutoBackupViewModel(BackupService backup, DatabaseService database)
    {
        _backup = backup;
        _database = database;
    }

    public ObservableCollection<BackupItem> Items { get; } = new();

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isEmpty;

    public void Load()
    {
        Items.Clear();
        var paths = _backup.ListAutoBackups();
        foreach (var path in paths)
        {
            Items.Add(new BackupItem(path, BackupFileNaming.FormatTimestamp(path, SvSe)));
        }
        IsEmpty = Items.Count == 0;
    }

    [RelayCommand]
    private async Task RestoreAsync(BackupItem? item)
    {
        if (item is null || IsBusy) return;

        var page = Shell.Current.CurrentPage;

        if (!File.Exists(item.Path))
        {
            await page.DisplayAlertAsync(
                "Backupen saknas",
                "Filen finns inte längre på disken. Listan uppdateras.",
                "OK").ConfigureAwait(true);
            Load();
            return;
        }

        var confirmed = await page.DisplayAlertAsync(
            "Återställ från auto-backup?",
            $"Återställ databasen till {item.TimestampLabel}? Detta ersätter ALL nuvarande data. Detta kan inte ångras.",
            "Ja, återställ",
            "Avbryt").ConfigureAwait(true);
        if (!confirmed) return;

        IsBusy = true;
        try
        {
            // Kopiera den valda backupen till en temp-fil INNAN vi auto-backar
            // det nuvarande läget. Annars riskerar pruning (10-fönstret) att
            // evicta just den fil vi precis ska återställa från: pre-restore-
            // backupen pushar listan till 11 → äldsta (möjligen item.Path)
            // raderas → ReplaceDatabaseAsync ser ingen fil.
            var tempCopy = Path.Combine(
                FileSystem.Current.CacheDirectory,
                $"restore-{Guid.NewGuid():N}.db");

            try
            {
                File.Copy(item.Path, tempCopy, overwrite: true);

                // Auto-backa nuvarande läget FÖRST så användaren kan rulla
                // tillbaka om återställningen blev fel. Synkront (inte
                // RequestBackup) — vi vill att filen finns innan vi skriver
                // över DB:n.
                await _backup.RunBackupNowAsync().ConfigureAwait(true);

                await _database.ReplaceDatabaseAsync(tempCopy).ConfigureAwait(true);
            }
            finally
            {
                if (File.Exists(tempCopy))
                {
                    try { File.Delete(tempCopy); } catch { /* ignorera städfel */ }
                }
            }

            // Samma signal som vid "Importera databas" — alla datadrivna
            // ViewModels prenumererar och kör om sin LoadAsync på UI-tråden.
            WeakReferenceMessenger.Default.Send(new DatabaseImportedMessage());

            await page.DisplayAlertAsync(
                "Databasen återställdes",
                $"Den aktiva databasen är nu {item.TimestampLabel}. En säkerhetskopia av föregående läge sparades i auto-backups.",
                "OK").ConfigureAwait(true);

            await Shell.Current.Navigation.PopModalAsync().ConfigureAwait(true);
        }
        catch (InvalidDataException ex)
        {
            await page.DisplayAlertAsync("Ogiltig backup", ex.Message, "OK").ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            await page.DisplayAlertAsync("Återställning misslyckades", ex.Message, "OK").ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private static async Task CloseAsync()
    {
        await Shell.Current.Navigation.PopModalAsync().ConfigureAwait(true);
    }
}

public sealed record BackupItem(string Path, string TimestampLabel);
