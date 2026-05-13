using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DoubleDashScore.Services;

namespace DoubleDashScore.ViewModels;

public partial class ApiKeySettingsViewModel : ObservableObject
{
    private readonly IApiKeyStore _keys;

    public ApiKeySettingsViewModel(IApiKeyStore keys)
    {
        _keys = keys;
    }

    [ObservableProperty]
    private string _entryText = string.Empty;

    [ObservableProperty]
    private string _currentStatus = "Ej konfigurerad";

    [ObservableProperty]
    private bool _isBusy;

    public async Task LoadAsync(CancellationToken ct = default)
    {
        var existing = await _keys.GetAsync(ct).ConfigureAwait(true);
        CurrentStatus = FormatStatus(existing);
        EntryText = string.Empty;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        var key = EntryText?.Trim();
        if (string.IsNullOrWhiteSpace(key))
        {
            await Shell.Current.CurrentPage.DisplayAlertAsync(
                "Tom nyckel",
                "Skriv in en API-nyckel innan du sparar.",
                "OK").ConfigureAwait(true);
            return;
        }

        IsBusy = true;
        try
        {
            await _keys.SetAsync(key).ConfigureAwait(true);
            CurrentStatus = FormatStatus(key);
            EntryText = string.Empty;
            await Shell.Current.CurrentPage.DisplayAlertAsync(
                "Sparad",
                "API-nyckeln är lagrad säkert på enheten.",
                "OK").ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ClearAsync()
    {
        var confirm = await Shell.Current.CurrentPage.DisplayAlertAsync(
            "Ta bort API-nyckel?",
            "OCR-flödet slutar fungera tills en ny nyckel sätts.",
            "Ta bort",
            "Avbryt").ConfigureAwait(true);
        if (!confirm) return;

        IsBusy = true;
        try
        {
            await _keys.SetAsync(null).ConfigureAwait(true);
            CurrentStatus = FormatStatus(null);
            EntryText = string.Empty;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string FormatStatus(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return "Ej konfigurerad";
        if (key.Length <= 12) return "Konfigurerad (kort nyckel)";
        return $"Konfigurerad: {key[..8]}…{key[^4..]}";
    }
}
