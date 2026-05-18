using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DoubleDashScore.Data;
using DoubleDashScore.Services;
using DoubleDashScore.Views;

namespace DoubleDashScore.ViewModels;

public partial class NightsListViewModel : ObservableObject
{
    private readonly GameNightRepository _nights;
    private readonly ExportService _export;
    private readonly PlayerRepository _players;
    private readonly HistoricalDataRepository _historical;

    public NightsListViewModel(
        GameNightRepository nights,
        ExportService export,
        PlayerRepository players,
        HistoricalDataRepository historical)
    {
        _nights = nights;
        _export = export;
        _players = players;
        _historical = historical;
    }

    public ObservableCollection<NightListItem> Items { get; } = new();

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isEmpty;

    public async Task LoadAsync(CancellationToken ct = default)
    {
        IsBusy = true;
        try
        {
            var summaries = await _nights.GetSummariesAsync(ct).ConfigureAwait(true);
            Items.Clear();
            foreach (var s in summaries)
            {
                Items.Add(new NightListItem(
                    s.Night.Id,
                    s.Night.PlayedOn.ToLocalTime().ToString("yyyy-MM-dd"),
                    s.Night.Note ?? string.Empty,
                    FormatRoundCount(s.RoundCount, s.CompleteRoundCount)));
            }
            IsEmpty = Items.Count == 0;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private static async Task NewNightAsync()
    {
        await Shell.Current.GoToAsync("NewNightPage").ConfigureAwait(true);
    }

    [RelayCommand]
    private static async Task OpenNightAsync(NightListItem? item)
    {
        if (item is null) return;
        await Shell.Current.GoToAsync($"NightDetailPage?nightId={item.Id}").ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task DeleteNightAsync(NightListItem? item)
    {
        if (item is null) return;
        var page = Shell.Current.CurrentPage;
        var confirm = await page.DisplayAlertAsync(
            "Ta bort kväll?",
            $"Kvällen {item.Date} mjukraderas (kan återskapas via databasen).",
            "Ta bort",
            "Avbryt").ConfigureAwait(true);
        if (!confirm) return;
        await _nights.SoftDeleteAsync(item.Id).ConfigureAwait(true);
        await LoadAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private static async Task EditPlayersAsync()
    {
        await Shell.Current.GoToAsync("PlayerEditPage").ConfigureAwait(true);
    }

    [RelayCommand]
    private static async Task OpenApiKeySettingsAsync()
    {
        await Shell.Current.GoToAsync("ApiKeySettingsPage").ConfigureAwait(true);
    }

    [RelayCommand]
    private static async Task OpenStatsAsync()
    {
        await Shell.Current.GoToAsync("HistoryStatsPage").ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task ExportAsync()
    {
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
    private async Task ImportAsync()
    {
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

            IsBusy = true;
            try
            {
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
            finally
            {
                IsBusy = false;
            }
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

    private static string FormatRoundCount(int total, int complete)
    {
        if (total == 0) return "Inga omgångar";
        return $"{total} omgång{(total == 1 ? "" : "ar")} ({complete} komplett{(complete == 1 ? "" : "a")})";
    }
}

public sealed record NightListItem(int Id, string Date, string Note, string RoundsSummary);
