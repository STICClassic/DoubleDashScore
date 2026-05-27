using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using DoubleDashScore.Data;
using DoubleDashScore.Services;
using DoubleDashScore.Views;

namespace DoubleDashScore.ViewModels;

// Globalt VM bakom Shell-flyout:en. Äger navigationen mellan rot-sidorna +
// import/export-kommandona som tidigare bodde på NightsListViewModel:s toolbar.
// AppShell.xaml.cs sätter BindingContext = denna och prenumererar på Navigated
// för att uppdatera SelectedRoute så att vald menypost kan styla sig själv.
public partial class AppShellViewModel : ObservableObject
{
    private readonly ExportService _export;
    private readonly PlayerRepository _players;
    private readonly HistoricalDataRepository _historical;
    private readonly DatabaseService _database;
    private readonly BackupService _backup;

    public AppShellViewModel(
        ExportService export,
        PlayerRepository players,
        HistoricalDataRepository historical,
        DatabaseService database,
        BackupService backup)
    {
        _export = export;
        _players = players;
        _historical = historical;
        _database = database;
        _backup = backup;
    }

    // Route-segmentet (sista delen av Shell.Current.CurrentState.Location.OriginalString)
    // för den sida flyout:en just nu pekar mot. Tomt vid uppstart innan första
    // Navigated-eventet.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsKvallarSelected))]
    [NotifyPropertyChangedFor(nameof(IsStatistikSelected))]
    [NotifyPropertyChangedFor(nameof(IsSpelareSelected))]
    [NotifyPropertyChangedFor(nameof(IsInstallningarSelected))]
    private string _selectedRoute = string.Empty;

    public bool IsKvallarSelected => SelectedRoute == nameof(NightsListPage);
    public bool IsStatistikSelected => SelectedRoute == nameof(HistoryStatsPage);
    public bool IsSpelareSelected => SelectedRoute == nameof(PlayerEditPage);
    public bool IsInstallningarSelected => SelectedRoute == nameof(ApiKeySettingsPage);

    // Toggle:as av "Importera/Exportera"-rubriken i flyout:en. Default kollapsad.
    [ObservableProperty]
    private bool _isImportExportExpanded;

    [RelayCommand]
    private void ToggleImportExport()
    {
        IsImportExportExpanded = !IsImportExportExpanded;
    }

    [RelayCommand]
    private static async Task NavigateAsync(string? route)
    {
        if (string.IsNullOrWhiteSpace(route)) return;
        Shell.Current.FlyoutIsPresented = false;
        // GoToAsync med absolut sökväg (// prefix) hoppar till roten av Shell:en
        // så vi inte staplar samma sida på stacken vid upprepade tap.
        await Shell.Current.GoToAsync($"//{route}").ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task ExportCsvAsync()
    {
        Shell.Current.FlyoutIsPresented = false;
        var page = Shell.Current.CurrentPage;
        try
        {
            var path = await _export.ExportAllNightsToCsvAsync().ConfigureAwait(true);
            await Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = "DoubleDash-Statistik",
                File = new ShareFile(path),
            }).ConfigureAwait(true);
        }
        catch (InvalidOperationException ex)
        {
            await page.DisplayAlertAsync("Kan inte exportera", ex.Message, "OK").ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private async Task ExportDatabaseAsync()
    {
        Shell.Current.FlyoutIsPresented = false;
        var page = Shell.Current.CurrentPage;
        try
        {
            var path = await _export.ExportDatabaseAsync().ConfigureAwait(true);
            await Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = "DoubleDash-databas (backup)",
                File = new ShareFile(path),
            }).ConfigureAwait(true);
        }
        catch (InvalidOperationException ex)
        {
            await page.DisplayAlertAsync("Kan inte exportera databas", ex.Message, "OK").ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private async Task ImportDatabaseAsync()
    {
        Shell.Current.FlyoutIsPresented = false;
        var page = Shell.Current.CurrentPage;
        try
        {
            // .db och .db3 finns inte i Androids standard-MIME-tabell — använd
            // octet-stream som ett brett filter. På desktop visar vi explicit
            // .db/.db3-filändelser.
            var fileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
            {
                [DevicePlatform.Android] = new[] { "application/octet-stream", "application/x-sqlite3", "application/vnd.sqlite3" },
                [DevicePlatform.WinUI] = new[] { ".db", ".db3" },
                [DevicePlatform.iOS] = new[] { "public.database", "public.data" },
                [DevicePlatform.macOS] = new[] { "db", "db3" },
            });

            var pick = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Välj .db-fil att importera",
                FileTypes = fileTypes,
            }).ConfigureAwait(true);
            if (pick is null) return;

            var confirmed = await page.DisplayAlertAsync(
                "Importera databas?",
                "Detta ersätter ALL nuvarande data med innehållet i filen. Detta kan inte ångras. Fortsätt?",
                "Ja, ersätt",
                "Avbryt").ConfigureAwait(true);
            if (!confirmed) return;

            // FilePicker ger oss en stream — kopiera till en lokal temp-fil först
            // så DatabaseService kan göra File.Copy mot något stabilt (vissa
            // platforms-handles går inte att läsa två gånger).
            var tempPath = Path.Combine(
                FileSystem.Current.CacheDirectory,
                $"import-{Guid.NewGuid():N}.db");
            try
            {
                await using (var src = await pick.OpenReadAsync().ConfigureAwait(true))
                await using (var dst = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await src.CopyToAsync(dst).ConfigureAwait(true);
                }

                await _database.ReplaceDatabaseAsync(tempPath).ConfigureAwait(true);
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    try { File.Delete(tempPath); } catch { /* ignorera städfel */ }
                }
            }

            // Trigga reload i alla datadrivna ViewModels som lyssnar. Skickar
            // synkront till aktiva prenumeranter; varje VM marshallar själv
            // till UI-tråden innan LoadAsync. Användaren stannar på sin
            // nuvarande vy så att t.ex. Statistik uppdateras in-place.
            WeakReferenceMessenger.Default.Send(new DatabaseImportedMessage());

            // Säkerställ att den importerade DB:n också hamnar i auto-backup-
            // fönstret — annars börjar rullande backupen om från noll nästa
            // gång användaren ändrar något.
            _backup.RequestBackup();

            await page.DisplayAlertAsync(
                "Databasen importerades",
                "Backupen är nu aktiv. Appen laddar om din data.",
                "OK").ConfigureAwait(true);
        }
        catch (InvalidDataException ex)
        {
            await page.DisplayAlertAsync("Ogiltig databasfil", ex.Message, "OK").ConfigureAwait(true);
        }
        catch (FileNotFoundException ex)
        {
            await page.DisplayAlertAsync("Filen hittades inte", ex.Message, "OK").ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            await page.DisplayAlertAsync("Importfel", ex.Message, "OK").ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private async Task ImportExcelAsync()
    {
        Shell.Current.FlyoutIsPresented = false;
        var page = Shell.Current.CurrentPage;
        try
        {
            var fileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
            {
                [DevicePlatform.Android] = new[] { "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" },
                [DevicePlatform.WinUI] = new[] { ".xlsx" },
                [DevicePlatform.iOS] = new[] { "org.openxmlformats.spreadsheetml.sheet" },
                [DevicePlatform.macOS] = new[] { "xlsx" },
            });

            var pick = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Välj XLSX-fil att importera",
                FileTypes = fileTypes,
            }).ConfigureAwait(true);
            if (pick is null) return;

            var players = await _players.GetActivePlayersAsync().ConfigureAwait(true);

            ParsedExcelImport parsed;
            await using (var stream = await pick.OpenReadAsync().ConfigureAwait(true))
            {
                parsed = await Task.Run(() => ExcelImporter.Parse(stream, players)).ConfigureAwait(true);
            }

            var preview = await _historical.ComputePreviewAsync(parsed).ConfigureAwait(true);
            var previewPage = new ImportPreviewPage(preview);
            await page.Navigation.PushModalAsync(previewPage).ConfigureAwait(true);
            var choice = await previewPage.Result.ConfigureAwait(true);
            if (choice is null) return;

            var result = await _historical.ImportAsync(parsed, choice.Overwrite).ConfigureAwait(true);
            await page.DisplayAlertAsync(
                "Import klar",
                BuildResultMessage(result),
                "OK").ConfigureAwait(true);
        }
        catch (InvalidDataException ex)
        {
            await page.DisplayAlertAsync("Ogiltig Excel-fil", ex.Message, "OK").ConfigureAwait(true);
        }
        catch (InvalidOperationException ex)
        {
            await page.DisplayAlertAsync("Kan inte importera", ex.Message, "OK").ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            await page.DisplayAlertAsync("Importfel", ex.Message, "OK").ConfigureAwait(true);
        }
    }

    private static string BuildResultMessage(ImportResult result)
    {
        var lines = new List<string>
        {
            $"{result.NightsInserted} nya kvällar importerade.",
        };
        if (result.NightsOverwritten > 0)
        {
            lines.Add($"{result.NightsOverwritten} befintliga kvällar uppdaterade.");
        }
        lines.Add($"{result.PlacementsInserted} placeringar skrivna.");
        lines.Add($"Totalscore ersatt: {(result.SnapshotReplaced ? "ja" : "nej")}.");
        return string.Join("\n", lines);
    }
}
