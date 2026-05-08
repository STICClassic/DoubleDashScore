using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DoubleDashScore.Data;

namespace DoubleDashScore.ViewModels;

public partial class NewNightViewModel : ObservableObject
{
    private readonly GameNightRepository _nights;

    public NewNightViewModel(GameNightRepository nights)
    {
        _nights = nights;
        PlayedOn = DateTime.Today;
    }

    [ObservableProperty]
    private DateTime _playedOn;

    [ObservableProperty]
    private string _note = string.Empty;

    [RelayCommand]
    private async Task SaveAsync()
    {
        var localMidnight = PlayedOn.Date;
        var utc = DateTime.SpecifyKind(localMidnight, DateTimeKind.Local).ToUniversalTime();
        var note = string.IsNullOrWhiteSpace(Note) ? null : Note.Trim();
        var id = await _nights.CreateAsync(utc, note).ConfigureAwait(true);
        await Shell.Current.GoToAsync($"../NightDetailPage?nightId={id}").ConfigureAwait(true);
    }

    [RelayCommand]
    private static async Task CancelAsync()
    {
        await Shell.Current.GoToAsync("..").ConfigureAwait(true);
    }
}
