using System.ComponentModel;
using System.Diagnostics;
using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DoubleDashScore.Data;
using DoubleDashScore.Services;

namespace DoubleDashScore.ViewModels;

[QueryProperty(nameof(GameNightId), "gameNightId")]
[QueryProperty(nameof(RoundId), "roundId")]
public partial class RoundEntryViewModel : ObservableObject
{
    private readonly PlayerRepository _playersRepo;
    private readonly RoundRepository _rounds;

    public RoundEntryViewModel(PlayerRepository players, RoundRepository rounds)
    {
        _playersRepo = players;
        _rounds = rounds;
        TrackCountText = "16";
    }

    [ObservableProperty]
    private int _gameNightId;

    [ObservableProperty]
    private int _roundId;

    [ObservableProperty]
    private string _title = "Ny omgång";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TrackCountValue))]
    [NotifyPropertyChangedFor(nameof(IsValid))]
    [NotifyPropertyChangedFor(nameof(ValidationMessage))]
    private string _trackCountText = "16";

    [ObservableProperty]
    private string _validationMessage = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _hasUnsavedChanges;

    [ObservableProperty]
    private bool _showErrors;

    private bool _suppressDirtyTracking;
    private bool _trackCountManuallyEdited;

    private static int _loadAsyncCallCount;
    private static int _onColumnChangedCallCount;
    private static int _liveColumnHandlerCount;

    private static readonly HashSet<string> IgnoredCellProperties = new()
    {
        nameof(PlayerColumnViewModel.FirstPlaceHasError),
        nameof(PlayerColumnViewModel.SecondPlaceHasError),
        nameof(PlayerColumnViewModel.ThirdPlaceHasError),
        nameof(PlayerColumnViewModel.FourthPlaceHasError),
    };

    [ObservableProperty]
    private IReadOnlyList<PlayerColumnViewModel> _players = Array.Empty<PlayerColumnViewModel>();

    public int TrackCountValue =>
        int.TryParse(TrackCountText, out var v) ? v : -1;

    public bool IsValid
    {
        get
        {
            var (ok, _) = RoundMatrixValidator.Validate(Players, TrackCountText);
            return ok;
        }
    }

    public async Task LoadAsync(CancellationToken ct = default)
    {
        var callId = Interlocked.Increment(ref _loadAsyncCallCount);
        var sw = Stopwatch.StartNew();
        Debug.WriteLine($"[LoadAsync #{callId}] START handlers-live={_liveColumnHandlerCount}");

        IsBusy = true;
        _suppressDirtyTracking = true;
        try
        {
            var activePlayers = await _playersRepo.GetActivePlayersAsync(ct).ConfigureAwait(true);
            Debug.WriteLine($"[LoadAsync #{callId}] t={sw.ElapsedMilliseconds}ms got {activePlayers.Count} players from DB");
            if (activePlayers.Count != 4)
            {
                ValidationMessage = $"Förväntade 4 aktiva spelare, hittade {activePlayers.Count}.";
                return;
            }

            RoundDetail? existing = null;
            if (RoundId > 0)
            {
                existing = await _rounds.GetRoundAsync(RoundId, ct).ConfigureAwait(true);
                if (existing is not null)
                {
                    Title = $"Redigera omgång {existing.Round.RoundNumber}";
                    TrackCountText = existing.Round.TrackCount.ToString();
                }
            }
            else
            {
                Title = "Ny omgång";
                TrackCountText = "16";
            }

            var newPlayers = new List<PlayerColumnViewModel>(4);
            foreach (var p in activePlayers)
            {
                var existingForPlayer = existing?.Results.FirstOrDefault(rr => rr.PlayerId == p.Id);
                var col = new PlayerColumnViewModel(p.Id, p.Name)
                {
                    FirstPlacesText = existingForPlayer?.FirstPlaces.ToString() ?? "0",
                    SecondPlacesText = existingForPlayer?.SecondPlaces.ToString() ?? "0",
                    ThirdPlacesText = existingForPlayer?.ThirdPlaces.ToString() ?? "0",
                    FourthPlacesText = existingForPlayer?.FourthPlaces.ToString() ?? "0",
                };
                col.PropertyChanged += OnColumnChanged;
                Interlocked.Increment(ref _liveColumnHandlerCount);
                newPlayers.Add(col);
            }

            DetachColumnHandlers();
            Players = newPlayers;
            Debug.WriteLine($"[LoadAsync #{callId}] t={sw.ElapsedMilliseconds}ms built+attached new Players, handlers-live={_liveColumnHandlerCount}");

            UpdateValidation();
        }
        finally
        {
            _suppressDirtyTracking = false;
            _trackCountManuallyEdited = false;
            HasUnsavedChanges = false;
            ShowErrors = false;
            IsBusy = false;
            Debug.WriteLine($"[LoadAsync #{callId}] DONE t={sw.ElapsedMilliseconds}ms");
        }
    }

    private void DetachColumnHandlers()
    {
        foreach (var p in Players)
        {
            p.PropertyChanged -= OnColumnChanged;
            Interlocked.Decrement(ref _liveColumnHandlerCount);
        }
    }

    public void Cleanup()
    {
        Debug.WriteLine($"[Cleanup] before: handlers-live={_liveColumnHandlerCount}");
        DetachColumnHandlers();
        Debug.WriteLine($"[Cleanup] after: handlers-live={_liveColumnHandlerCount}");
    }

    private void OnColumnChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null || IgnoredCellProperties.Contains(e.PropertyName)) return;

        var callId = Interlocked.Increment(ref _onColumnChangedCallCount);
        Debug.WriteLine($"[OnColumnChanged #{callId}] property={e.PropertyName} suppressed={_suppressDirtyTracking}");

        if (!_suppressDirtyTracking)
        {
            HasUnsavedChanges = true;
            if (ReferenceEquals(sender, Players.FirstOrDefault()))
            {
                AutoUpdateTrackCount();
            }
        }
        UpdateValidation();
        UpdateErrorCells();
    }

    partial void OnTrackCountTextChanged(string value)
    {
        if (!_suppressDirtyTracking)
        {
            _trackCountManuallyEdited = true;
            HasUnsavedChanges = true;
        }
        UpdateValidation();
        UpdateErrorCells();
    }

    partial void OnShowErrorsChanged(bool value) => UpdateErrorCells();

    private void AutoUpdateTrackCount()
    {
        if (_trackCountManuallyEdited) return;
        if (Players.Count == 0) return;
        if (!Players[0].TryGetCounts(out var c)) return;
        var newText = (c.first + c.second + c.third + c.fourth).ToString();
        if (TrackCountText == newText) return;

        _suppressDirtyTracking = true;
        try
        {
            TrackCountText = newText;
        }
        finally
        {
            _suppressDirtyTracking = false;
        }
    }

    private void UpdateValidation()
    {
        var (_, message) = RoundMatrixValidator.Validate(Players, TrackCountText);
        ValidationMessage = message;
        OnPropertyChanged(nameof(IsValid));
        SaveCommand.NotifyCanExecuteChanged();
    }

    private void UpdateErrorCells()
    {
        if (Players.Count != 4) return;

        if (!ShowErrors)
        {
            for (int i = 0; i < 4; i++) Players[i].SetCellErrors(MatrixErrorDetector.CellErrors.None);
            return;
        }

        var cells = new List<MatrixErrorDetector.MatrixCells>(4);
        foreach (var p in Players)
        {
            p.TryGetCounts(out var c);
            cells.Add(new MatrixErrorDetector.MatrixCells(c.first, c.second, c.third, c.fourth));
        }
        int.TryParse(TrackCountText, out var tracks);
        var errors = MatrixErrorDetector.Detect(cells, tracks);
        for (int i = 0; i < 4; i++)
        {
            Players[i].SetCellErrors(errors[i]);
        }
        if (errors.All(e => e == MatrixErrorDetector.CellErrors.None))
        {
            ShowErrors = false;
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (!IsValid)
        {
            ShowErrors = true;
            return;
        }
        var trackCount = TrackCountValue;
        var inputs = Players.Select(p =>
        {
            p.TryGetCounts(out var c);
            return new RoundResultInput(p.PlayerId, c.first, c.second, c.third, c.fourth);
        }).ToList();

        IsBusy = true;
        try
        {
            if (RoundId > 0)
            {
                await _rounds.UpdateRoundAsync(RoundId, trackCount, inputs).ConfigureAwait(true);
            }
            else
            {
                await _rounds.CreateRoundAsync(GameNightId, trackCount, inputs).ConfigureAwait(true);
            }
            HasUnsavedChanges = false;
            await Shell.Current.GoToAsync("..").ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<bool> ConfirmDiscardAsync()
    {
        if (!HasUnsavedChanges) return true;
        var page = Shell.Current.CurrentPage;
        return await page.DisplayAlertAsync(
            "Osparade ändringar",
            "Du har osparade ändringar. Vill du avbryta?",
            "Ja, avbryt",
            "Nej, fortsätt").ConfigureAwait(true);
    }

    public async Task TryNavigateBackAsync()
    {
        if (!await ConfirmDiscardAsync().ConfigureAwait(true)) return;
        await Shell.Current.GoToAsync("..").ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task CancelAsync() => await TryNavigateBackAsync().ConfigureAwait(true);

    [RelayCommand]
    private async Task BackAsync() => await TryNavigateBackAsync().ConfigureAwait(true);
}
