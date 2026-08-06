using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DoubleDashScore.Services;
using DoubleDashScore.Views;

namespace DoubleDashScore.ViewModels;

public partial class ApiKeySettingsViewModel : ObservableObject
{
    private static readonly CultureInfo SvSe = CultureInfo.GetCultureInfo("sv-SE");

    private readonly IApiKeyStore _keys;
    private readonly BackupService _backup;
    private readonly GitHubWebSyncService _webSync;
    private readonly IServiceProvider _services;

    public ApiKeySettingsViewModel(
        IApiKeyStore keys,
        BackupService backup,
        GitHubWebSyncService webSync,
        IServiceProvider services)
    {
        _keys = keys;
        _backup = backup;
        _webSync = webSync;
        _services = services;
    }

    [ObservableProperty]
    private string _entryText = string.Empty;

    [ObservableProperty]
    private string _currentStatus = "Ej konfigurerad";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _backupLatestLine = "Senaste auto-backup: –";

    [ObservableProperty]
    private string _backupCountLine = "0 sparade backups";

    [ObservableProperty]
    private bool _hasBackups;

    [ObservableProperty]
    private string _gitHubTokenText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowChangeTokenLink))]
    private bool _hasGitHubToken;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowChangeTokenLink))]
    private bool _showGitHubTokenEntry = true;

    /// <summary>"Byt token" är bara meningsfullt när fältet är dolt.</summary>
    public bool ShowChangeTokenLink => HasGitHubToken && !ShowGitHubTokenEntry;

    [ObservableProperty]
    private string _webSyncSubtitle = NoTokenSubtitle;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PublishButtonText))]
    private bool _isPublishing;

    public string PublishButtonText => IsPublishing ? "Uppdaterar…" : "Uppdatera hemsidan";

    private const string NoTokenSubtitle = "Uppdatera hemsidan när du lagt in nya kvällar.";

    private static readonly string HasTokenSubtitle =
        $"Uppdaterar {GitHubWebSyncService.SiteUrl}";

    public async Task LoadAsync(CancellationToken ct = default)
    {
        var existing = await _keys.GetAsync(ct).ConfigureAwait(true);
        CurrentStatus = FormatStatus(existing);
        EntryText = string.Empty;
        RefreshBackupInfo();
        await RefreshWebSyncStateAsync(ct).ConfigureAwait(true);
    }

    private async Task RefreshWebSyncStateAsync(CancellationToken ct = default)
    {
        HasGitHubToken = await _webSync.HasTokenAsync(ct).ConfigureAwait(true);
        ShowGitHubTokenEntry = !HasGitHubToken;
        GitHubTokenText = string.Empty;
        WebSyncSubtitle = HasGitHubToken ? HasTokenSubtitle : NoTokenSubtitle;
    }

    private void RefreshBackupInfo()
    {
        var backups = _backup.ListAutoBackups();
        HasBackups = backups.Count > 0;
        if (backups.Count == 0)
        {
            BackupLatestLine = "Inga auto-backups än";
            BackupCountLine = "0 sparade backups";
            return;
        }

        // ListAutoBackups returnerar nyast → äldst, så index 0 är senaste.
        BackupLatestLine = $"Senaste auto-backup: {BackupFileNaming.FormatTimestamp(backups[0], SvSe)}";
        BackupCountLine = backups.Count == 1
            ? "1 sparad backup"
            : $"{backups.Count} sparade backups";
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

    [RelayCommand]
    private async Task SaveGitHubTokenAsync()
    {
        var token = GitHubTokenText?.Trim();
        if (string.IsNullOrWhiteSpace(token))
        {
            await Shell.Current.CurrentPage.DisplayAlertAsync(
                "Tom token",
                "Skriv in en GitHub-token innan du sparar.",
                "OK").ConfigureAwait(true);
            return;
        }

        IsBusy = true;
        try
        {
            await _webSync.SaveTokenAsync(token).ConfigureAwait(true);
            await RefreshWebSyncStateAsync().ConfigureAwait(true);
            await Shell.Current.CurrentPage.DisplayAlertAsync(
                "Sparad",
                "GitHub-tokenen är lagrad säkert på enheten.",
                "OK").ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void ChangeGitHubToken()
    {
        GitHubTokenText = string.Empty;
        ShowGitHubTokenEntry = true;
    }

    [RelayCommand]
    private async Task PublishWebsiteAsync()
    {
        var confirm = await Shell.Current.CurrentPage.DisplayAlertAsync(
            "Uppdatera hemsidan",
            "Publicera nuvarande databas till hemsidan?",
            "Ja",
            "Avbryt").ConfigureAwait(true);
        if (!confirm) return;

        IsPublishing = true;
        try
        {
            var result = await _webSync.PublishDatabaseAsync().ConfigureAwait(true);

            if (result.TokenRejected)
            {
                // Tokenen dög inte — visa inmatningsfältet igen direkt.
                GitHubTokenText = string.Empty;
                ShowGitHubTokenEntry = true;
            }

            await Shell.Current.CurrentPage.DisplayAlertAsync(
                result.Success ? "Klart" : "Kunde inte uppdatera",
                result.Message,
                "OK").ConfigureAwait(true);
        }
        finally
        {
            IsPublishing = false;
        }
    }

    [RelayCommand]
    private async Task OpenRestoreAsync()
    {
        // Hämta sidan via DI så att VM:n får sina dependencies konstruerade.
        // När modalen pop:as kör ApiKeySettingsPage.OnAppearing → LoadAsync,
        // vilket uppdaterar backup-info-raden automatiskt.
        var page = (RestoreAutoBackupPage)_services.GetService(typeof(RestoreAutoBackupPage))!;
        await Shell.Current.Navigation.PushModalAsync(page).ConfigureAwait(true);
    }

    private static string FormatStatus(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return "Ej konfigurerad";
        if (key.Length <= 12) return "Konfigurerad (kort nyckel)";
        return $"Konfigurerad: {key[..8]}…{key[^4..]}";
    }
}
