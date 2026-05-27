using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using DoubleDashScore.Data;
using DoubleDashScore.Services;

namespace DoubleDashScore.ViewModels;

public partial class NightsListViewModel : ObservableObject, IRecipient<DatabaseImportedMessage>
{
    private static readonly CultureInfo SvSe = CultureInfo.GetCultureInfo("sv-SE");

    private readonly GameNightRepository _nights;

    public NightsListViewModel(GameNightRepository nights)
    {
        _nights = nights;
        WeakReferenceMessenger.Default.Register(this);
    }

    // Triggas när AppShellViewModel.ImportDatabaseAsync skickar signalen efter
    // en lyckad .db-import. Marshall till UI-tråden eftersom LoadAsync rör
    // ObservableCollection och anroparen kan vara på vilken tråd som helst.
    public void Receive(DatabaseImportedMessage message)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            try { await LoadAsync().ConfigureAwait(true); }
            catch { /* fire-and-forget: nästa OnAppearing laddar om igen */ }
        });
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
                    s.Night.PlayedOn.ToLocalTime().ToString("d MMMM yyyy", SvSe),
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
            $"Är du säker på att du vill ta bort kväll {item.Date}?",
            "Ja",
            "Avbryt").ConfigureAwait(true);
        if (!confirm) return;
        await _nights.SoftDeleteAsync(item.Id).ConfigureAwait(true);
        await LoadAsync().ConfigureAwait(true);
    }

    private static string FormatRoundCount(int total, int complete)
    {
        if (total == 0) return "Inga omgångar";
        return $"{total} omgång{(total == 1 ? "" : "ar")} ({complete} komplett{(complete == 1 ? "" : "a")})";
    }
}

public sealed record NightListItem(int Id, string Date, string Note, string RoundsSummary);
