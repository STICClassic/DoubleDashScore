using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using DoubleDashScore.Data;
using DoubleDashScore.Services;

namespace DoubleDashScore.ViewModels;

// Modal redigering av en kvälls anteckning. Nås från NightDetailPage genom
// att tappa anteckningen (eller "Lägg till anteckning"-platshållaren).
// nightId kommer via query; nuvarande Note läses från DB i LoadAsync så att
// vi slipper URL-koda flerradig/lång text genom navigeringen.
[QueryProperty(nameof(NightId), "nightId")]
public partial class EditNoteViewModel : ObservableObject
{
    // Matchar GameNight.Note [MaxLength(500)]. Editorn hård-kapar via
    // MaxLength, räknaren visar hur mycket som är kvar.
    public const int MaxNoteLength = 500;

    private readonly GameNightRepository _nights;

    public EditNoteViewModel(GameNightRepository nights)
    {
        _nights = nights;
    }

    [ObservableProperty]
    private int _nightId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CounterText))]
    private string _note = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    public int MaxLength => MaxNoteLength;

    public string CounterText => $"{Note?.Length ?? 0}/{MaxNoteLength}";

    public async Task LoadAsync(CancellationToken ct = default)
    {
        if (NightId <= 0) return;
        IsBusy = true;
        try
        {
            var night = await _nights.GetAsync(NightId, ct).ConfigureAwait(true);
            Note = night?.Note ?? string.Empty;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (NightId <= 0)
        {
            await Shell.Current.GoToAsync("..").ConfigureAwait(true);
            return;
        }

        // Trimma; tom sträng ⇒ null (anteckning tas effektivt bort).
        var trimmed = string.IsNullOrWhiteSpace(Note) ? null : Note.Trim();
        await _nights.UpdateNoteAsync(NightId, trimmed).ConfigureAwait(true);

        // Riktad in-place-uppdatering av detaljvyn + listan (oberoende av
        // OnAppearing-timing när modalen stängs).
        WeakReferenceMessenger.Default.Send(new GameNightNoteUpdatedMessage(NightId, trimmed));

        await Shell.Current.GoToAsync("..").ConfigureAwait(true);
    }

    [RelayCommand]
    private static async Task CancelAsync()
    {
        await Shell.Current.GoToAsync("..").ConfigureAwait(true);
    }
}
