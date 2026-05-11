using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DoubleDashScore.Data;
using DoubleDashScore.Services;

namespace DoubleDashScore.ViewModels;

public partial class NightsListViewModel : ObservableObject
{
    private readonly GameNightRepository _nights;
    private readonly ExportService _export;

    public NightsListViewModel(GameNightRepository nights, ExportService export)
    {
        _nights = nights;
        _export = export;
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

    private static string FormatRoundCount(int total, int complete)
    {
        if (total == 0) return "Inga omgångar";
        return $"{total} omgång{(total == 1 ? "" : "ar")} ({complete} komplett{(complete == 1 ? "" : "a")})";
    }
}

public sealed record NightListItem(int Id, string Date, string Note, string RoundsSummary);
