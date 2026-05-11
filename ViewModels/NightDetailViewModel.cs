using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DoubleDashScore.Data;

namespace DoubleDashScore.ViewModels;

[QueryProperty(nameof(NightId), "nightId")]
public partial class NightDetailViewModel : ObservableObject
{
    private readonly GameNightRepository _nights;
    private readonly RoundRepository _rounds;

    public NightDetailViewModel(GameNightRepository nights, RoundRepository rounds)
    {
        _nights = nights;
        _rounds = rounds;
    }

    [ObservableProperty]
    private int _nightId;

    [ObservableProperty]
    private string _title = "Kväll";

    [ObservableProperty]
    private string _note = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    public ObservableCollection<RoundListItem> Rounds { get; } = new();

    public async Task LoadAsync(CancellationToken ct = default)
    {
        if (NightId <= 0) return;
        IsBusy = true;
        try
        {
            var night = await _nights.GetAsync(NightId, ct).ConfigureAwait(true);
            if (night is null)
            {
                Title = "Kväll (saknas)";
                Note = string.Empty;
                Rounds.Clear();
                return;
            }
            Title = $"Kväll {night.PlayedOn.ToLocalTime():yyyy-MM-dd}";
            Note = night.Note ?? string.Empty;

            var details = await _rounds.GetRoundsForNightAsync(NightId, ct).ConfigureAwait(true);
            Rounds.Clear();
            foreach (var d in details)
            {
                var label = $"Omgång {d.Round.RoundNumber} — {d.Results.Count}/4 spelare, {d.Round.TrackCount} banor";
                if (d.IsComplete) label += " ✓";
                Rounds.Add(new RoundListItem(d.Round.Id, label));
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task AddRoundAsync()
    {
        await Shell.Current.GoToAsync($"RoundEntryPage?gameNightId={NightId}").ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task OpenStatsAsync()
    {
        await Shell.Current.GoToAsync($"NightStatsPage?nightId={NightId}").ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task OpenRoundAsync(RoundListItem? item)
    {
        if (item is null) return;
        await Shell.Current.GoToAsync($"RoundEntryPage?gameNightId={NightId}&roundId={item.Id}").ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task DeleteRoundAsync(RoundListItem? item)
    {
        if (item is null) return;
        var page = Shell.Current.CurrentPage;
        var confirm = await page.DisplayAlertAsync(
            "Ta bort omgång?",
            "Omgången mjukraderas.",
            "Ta bort",
            "Avbryt").ConfigureAwait(true);
        if (!confirm) return;
        await _rounds.SoftDeleteRoundAsync(item.Id).ConfigureAwait(true);
        await LoadAsync().ConfigureAwait(true);
    }
}

public sealed record RoundListItem(int Id, string Label);
