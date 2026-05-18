using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DoubleDashScore.Data;
using DoubleDashScore.Models;
using DoubleDashScore.Services;

namespace DoubleDashScore.ViewModels;

public partial class OcrPreviewViewModel : ObservableObject
{
    private readonly PlayerRepository _playersRepo;
    private readonly RoundRepository _rounds;
    private readonly OcrFlowContext _context;

    public OcrPreviewViewModel(
        PlayerRepository players,
        RoundRepository rounds,
        OcrFlowContext context)
    {
        _playersRepo = players;
        _rounds = rounds;
        _context = context;
    }

    [ObservableProperty]
    private IReadOnlyList<PlayerColumnViewModel> _players = Array.Empty<PlayerColumnViewModel>();

    public ObservableCollection<Player> AvailablePlayers { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsValid))]
    [NotifyPropertyChangedFor(nameof(ValidationMessage))]
    private string _trackCountText = "16";

    [ObservableProperty]
    private string _validationMessage = string.Empty;

    [ObservableProperty]
    private string _warningsBanner = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsValid))]
    private Player? _selectedPlayer0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsValid))]
    private Player? _selectedPlayer1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsValid))]
    private Player? _selectedPlayer2;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsValid))]
    private Player? _selectedPlayer3;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _hasUnsavedChanges;

    private bool _suppressDirtyTracking;
    private bool _trackCountManuallyEdited;
    private int _columnChangeCount;
    private int _trackCountChangeCount;
    private int _selectionChangeCount;

    public bool IsValid
    {
        get
        {
            var (matrixOk, _) = RoundMatrixValidator.Validate(Players, TrackCountText);
            var (mapOk, _) = MappingValidator.Validate(CurrentSelections());
            return matrixOk && mapOk;
        }
    }

    public async Task LoadAsync(CancellationToken ct = default)
    {
        IsBusy = true;
        _suppressDirtyTracking = true;
        try
        {
            AvailablePlayers.Clear();

            var active = await _playersRepo.GetActivePlayersAsync(ct).ConfigureAwait(true);
            if (active.Count != 4)
            {
                ValidationMessage = $"Förväntade 4 aktiva spelare, hittade {active.Count}.";
                return;
            }
            foreach (var p in active) AvailablePlayers.Add(p);

            var parsed = _context.Pending;
            if (parsed is null)
            {
                ValidationMessage = "Inga OCR-data att förhandsgranska.";
                return;
            }

            var defaultMapping = PlayerSlotMapper.Map(active);
            SelectedPlayer0 = defaultMapping[0];
            SelectedPlayer1 = defaultMapping[1];
            SelectedPlayer2 = defaultMapping[2];
            SelectedPlayer3 = defaultMapping[3];

            var newPlayers = new List<PlayerColumnViewModel>(4);
            for (int i = 0; i < 4; i++)
            {
                var slot = parsed.Slots[i];
                var col = new PlayerColumnViewModel(0, $"P{i + 1}")
                {
                    FirstPlacesText = slot.FirstPlaces.ToString(),
                    SecondPlacesText = slot.SecondPlaces.ToString(),
                    ThirdPlacesText = slot.ThirdPlaces.ToString(),
                    FourthPlacesText = slot.FourthPlaces.ToString(),
                };
                col.PropertyChanged += OnColumnChanged;
                newPlayers.Add(col);
            }

            DetachColumnHandlers();
            Players = newPlayers;

            TrackCountText = parsed.InferredTrackCount > 0
                ? parsed.InferredTrackCount.ToString()
                : "16";

            WarningsBanner = parsed.Warnings.Count == 0
                ? "Förifyllt från foto."
                : $"Förifyllt från foto ({parsed.Warnings.Count} varning{(parsed.Warnings.Count == 1 ? "" : "ar")}): {string.Join(" ", parsed.Warnings)}";

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
        foreach (var p in Players) p.PropertyChanged -= OnColumnChanged;
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
        var (_, matrixMsg) = RoundMatrixValidator.Validate(Players, TrackCountText);
        var (mapOk, mapMsg) = MappingValidator.Validate(CurrentSelections());
        ValidationMessage = mapOk ? matrixMsg : $"{mapMsg}  {matrixMsg}";
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

    private void MarkSelectionDirty()
    {
        if (_suppressDirtyTracking) return;
        _selectionChangeCount++;
        HasUnsavedChanges = true;
    }

    partial void OnSelectedPlayer0Changed(Player? value) { UpdateValidation(); MarkSelectionDirty(); }
    partial void OnSelectedPlayer1Changed(Player? value) { UpdateValidation(); MarkSelectionDirty(); }
    partial void OnSelectedPlayer2Changed(Player? value) { UpdateValidation(); MarkSelectionDirty(); }
    partial void OnSelectedPlayer3Changed(Player? value) { UpdateValidation(); MarkSelectionDirty(); }

    private IReadOnlyList<Player?> CurrentSelections() =>
        new[] { SelectedPlayer0, SelectedPlayer1, SelectedPlayer2, SelectedPlayer3 };

    [RelayCommand(CanExecute = nameof(IsValid))]
    private async Task SaveAsync()
    {
        if (!IsValid) return;
        var selections = CurrentSelections();
        var trackCount = int.Parse(TrackCountText);
        var inputs = new List<RoundResultInput>(4);
        for (int i = 0; i < 4; i++)
        {
            Players[i].TryGetCounts(out var c);
            inputs.Add(new RoundResultInput(selections[i]!.Id, c.first, c.second, c.third, c.fourth));
        }

        IsBusy = true;
        try
        {
            await _rounds.CreateRoundAsync(
                _context.GameNightId,
                trackCount,
                inputs,
                photoPath: _context.PhotoPath).ConfigureAwait(true);
            _context.Clear();
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
            $"_selectionChangeCount = {_selectionChangeCount}\n" +
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
        _context.Clear();
        await Shell.Current.GoToAsync("..").ConfigureAwait(true);
    }
}
