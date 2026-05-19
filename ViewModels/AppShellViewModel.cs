using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

    public AppShellViewModel(
        ExportService export,
        PlayerRepository players,
        HistoricalDataRepository historical)
    {
        _export = export;
        _players = players;
        _historical = historical;
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
