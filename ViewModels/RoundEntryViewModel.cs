using System.ComponentModel;
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

    private bool _suppressDirtyTracking;
    private bool _trackCountManuallyEdited;
    private int _columnChangeCount;
    private int _trackCountChangeCount;

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
        IsBusy = true;
        _suppressDirtyTracking = true;
        try
        {
            var activePlayers = await _playersRepo.GetActivePlayersAsync(ct).ConfigureAwait(true);
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
                newPlayers.Add(col);
            }

            DetachColumnHandlers();
            Players = newPlayers;

            UpdateValidation();
        }
        finally
        {
            _suppressDirtyTracking = false;
            _trackCountManuallyEdited = false;
            HasUnsavedChanges = false;
            IsBusy = false;
        }
    }

    private void DetachColumnHandlers()
    {
        foreach (var p in Players)
        {
            p.PropertyChanged -= OnColumnChanged;
        }
    }

    private void OnColumnChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!_suppressDirtyTracking)
        {
            _columnChangeCount++;
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
            _trackCountChangeCount++;
            _trackCountManuallyEdited = true;
            HasUnsavedChanges = true;
        }
        UpdateValidation();
        UpdateErrorCells();
    }

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
    }

    [RelayCommand(CanExecute = nameof(IsValid))]
    private async Task SaveAsync()
    {
        if (!IsValid) return;
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

    public async Task<bool> ConfirmDiscardAsync(string trigger)
    {
        var page = Shell.Current.CurrentPage;
        var p1 = Players.Count > 0 ? Players[0] : null;
        await page.DisplayAlertAsync(
            $"[DEBUG] {trigger}",
            $"HasUnsavedChanges = {HasUnsavedChanges}\n" +
            $"_suppressDirtyTracking = {_suppressDirtyTracking}\n" +
            $"_columnChangeCount = {_columnChangeCount}\n" +
            $"_trackCountChangeCount = {_trackCountChangeCount}\n" +
            $"_trackCountManuallyEdited = {_trackCountManuallyEdited}\n" +
            $"Players.Count = {Players.Count}\n" +
            $"TrackCountText = {TrackCountText}\n" +
            $"P1 first/second/third/fourth = " +
            $"{p1?.FirstPlacesText}/{p1?.SecondPlacesText}/{p1?.ThirdPlacesText}/{p1?.FourthPlacesText}",
            "OK").ConfigureAwait(true);

        if (!HasUnsavedChanges) return true;
        return await page.DisplayAlertAsync(
            "Osparade ändringar",
            "Du har osparade ändringar. Vill du avbryta?",
            "Ja, avbryt",
            "Nej, fortsätt").ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task CancelAsync()
    {
        if (!await ConfirmDiscardAsync("Avbryt-knapp").ConfigureAwait(true)) return;
        await Shell.Current.GoToAsync("..").ConfigureAwait(true);
    }
}
