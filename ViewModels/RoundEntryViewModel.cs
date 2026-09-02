using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DoubleDashScore.Data;
using DoubleDashScore.Models;
using DoubleDashScore.Services;

namespace DoubleDashScore.ViewModels;

[QueryProperty(nameof(GameNightId), "gameNightId")]
[QueryProperty(nameof(RoundId), "roundId")]
public partial class RoundEntryViewModel : ObservableObject
{
    private readonly PlayerRepository _playersRepo;
    private readonly RoundRepository _rounds;
    private readonly IPlayerPositionMappingStore _mappingStore;

    public RoundEntryViewModel(
        PlayerRepository players,
        RoundRepository rounds,
        IPlayerPositionMappingStore mappingStore)
    {
        _playersRepo = players;
        _rounds = rounds;
        _mappingStore = mappingStore;
        TrackCountText = "16";
    }

    /// <summary>Spelarna att välja mellan när en positions rubrik tap:as.</summary>
    public ObservableCollection<Player> AvailablePlayers { get; } = new();

    /// <summary>Vem som sitter på position P1-P4 just nu. Alltid en permutation.</summary>
    private IReadOnlyList<Player?> _slots = new Player?[4];

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

    private static readonly HashSet<string> IgnoredCellProperties = new()
    {
        nameof(PlayerColumnViewModel.FirstPlaceHasError),
        nameof(PlayerColumnViewModel.SecondPlaceHasError),
        nameof(PlayerColumnViewModel.ThirdPlaceHasError),
        nameof(PlayerColumnViewModel.FourthPlaceHasError),
        // Rubrikbytet hanteras av AssignPlayer, inte av cell-handlern.
        nameof(PlayerColumnViewModel.PlayerId),
        nameof(PlayerColumnViewModel.PlayerName),
        nameof(PlayerColumnViewModel.NameColor),
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
        IsBusy = true;
        _suppressDirtyTracking = true;
        try
        {
            AvailablePlayers.Clear();

            var activePlayers = await _playersRepo.GetActivePlayersAsync(ct).ConfigureAwait(true);
            if (activePlayers.Count != 4)
            {
                ValidationMessage = $"Förväntade 4 aktiva spelare, hittade {activePlayers.Count}.";
                return;
            }
            foreach (var p in activePlayers) AvailablePlayers.Add(p);

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

            // Ny omgång: den globala mappningen från senaste inmatningen
            // (manuell eller OCR). Redigering: omgångens egna resultatrader —
            // deras ordning är sanningen för just den omgången, annars skulle
            // headern ljuga om vems siffror som står i kolumnen.
            var slotIds = RoundSlotOrder.SlotIdsFor(existing?.Results, _mappingStore.Get());
            var mapping = PlayerSlotMapper.Resolve(activePlayers, slotIds);
            _slots = mapping.Cast<Player?>().ToList();

            var newPlayers = new List<PlayerColumnViewModel>(4);
            for (int i = 0; i < 4; i++)
            {
                var p = mapping[i];
                var existingForPlayer = existing?.Results.FirstOrDefault(rr => rr.PlayerId == p.Id);
                var col = new PlayerColumnViewModel(p.Id, p.Name, i)
                {
                    NameColor = PlayerColors.MauiColorFor(p.Name),
                    FirstPlacesText = existingForPlayer?.FirstPlaces.ToString() ?? "0",
                    SecondPlacesText = existingForPlayer?.SecondPlaces.ToString() ?? "0",
                    ThirdPlacesText = existingForPlayer?.ThirdPlaces.ToString() ?? "0",
                    FourthPlacesText = existingForPlayer?.FourthPlaces.ToString() ?? "0",
                };
                newPlayers.Add(col);
            }

            DetachColumnHandlers();
            Players = newPlayers;

            foreach (var col in Players) col.PropertyChanged += OnColumnChanged;

            UpdateValidation();
            UpdateErrorCells();
        }
        finally
        {
            _suppressDirtyTracking = false;
            _trackCountManuallyEdited = false;
            HasUnsavedChanges = false;
            ShowErrors = false;
            IsBusy = false;
        }
    }

    private void DetachColumnHandlers()
    {
        foreach (var p in Players) p.PropertyChanged -= OnColumnChanged;
    }

    public void Cleanup() => DetachColumnHandlers();

    private void OnColumnChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null || IgnoredCellProperties.Contains(e.PropertyName)) return;
        if (_suppressDirtyTracking) return;

        HasUnsavedChanges = true;
        if (ReferenceEquals(sender, Players.FirstOrDefault()))
        {
            AutoUpdateTrackCount();
        }
        UpdateValidation();
        UpdateErrorCells();
    }

    partial void OnTrackCountTextChanged(string value)
    {
        if (_suppressDirtyTracking) return;
        _trackCountManuallyEdited = true;
        HasUnsavedChanges = true;
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

    /// <summary>
    /// Tap på ett spelarnamn i rubrikraden: välj vem som satt på positionen.
    /// Siffrorna står kvar i kolumnen — det är namnet som flyttar sig.
    /// </summary>
    [RelayCommand]
    private async Task PickPlayerAsync(PlayerColumnViewModel? column)
    {
        if (column is null) return;
        if (AvailablePlayers.Count != 4) return;

        var page = Shell.Current.CurrentPage;
        var names = AvailablePlayers.Select(p => p.Name).ToArray();
        var choice = await page.DisplayActionSheetAsync(
            $"Vem satt på P{column.SlotIndex + 1}?",
            "Avbryt",
            null,
            names).ConfigureAwait(true);

        var chosen = AvailablePlayers.FirstOrDefault(p => p.Name == choice);
        if (chosen is null) return;

        AssignPlayer(column.SlotIndex, chosen);
    }

    /// <summary>
    /// Sätter spelaren på positionen. Satt hen redan på en annan position byter
    /// de två plats (se <see cref="PlayerSlotMapper.Assign"/>).
    /// </summary>
    private void AssignPlayer(int slotIndex, Player chosen)
    {
        _slots = PlayerSlotMapper.Assign(_slots, slotIndex, chosen);
        SyncColumnHeaders();
        HasUnsavedChanges = true;
        UpdateValidation();
    }

    private void SyncColumnHeaders()
    {
        if (Players.Count != 4) return;
        for (int i = 0; i < 4; i++)
        {
            var player = _slots[i];
            if (player is null) continue;
            Players[i].PlayerId = player.Id;
            Players[i].PlayerName = player.Name;
            Players[i].NameColor = PlayerColors.MauiColorFor(player.Name);
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

            // Nästa inmatning — manuell eller OCR — öppnar med samma mappning.
            // Bara ny omgång eller redigering av senaste omgången får skriva om
            // den; en gammal omgångs mappning är historisk (se
            // MappingPersistenceRule).
            if (_slots.All(p => p is not null) && await ShouldPersistMappingAsync().ConfigureAwait(true))
            {
                _mappingStore.Set(_slots.Select(p => p!.Id).ToList());
            }

            HasUnsavedChanges = false;
            await Shell.Current.GoToAsync("..").ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task<bool> ShouldPersistMappingAsync()
    {
        if (RoundId <= 0) return true;
        var latestRoundId = await _rounds.GetLatestRoundIdAsync().ConfigureAwait(true);
        return MappingPersistenceRule.ShouldPersist(RoundId, latestRoundId);
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
