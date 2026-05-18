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
        UpdateValidation();
        if (!_suppressDirtyTracking) HasUnsavedChanges = true;
    }

    private void UpdateValidation()
    {
        var (_, matrixMsg) = RoundMatrixValidator.Validate(Players, TrackCountText);
        var (mapOk, mapMsg) = MappingValidator.Validate(CurrentSelections());
        ValidationMessage = mapOk ? matrixMsg : $"{mapMsg}  {matrixMsg}";
        OnPropertyChanged(nameof(IsValid));
        SaveCommand.NotifyCanExecuteChanged();
    }

    private void MarkDirty()
    {
        if (!_suppressDirtyTracking) HasUnsavedChanges = true;
    }

    partial void OnTrackCountTextChanged(string value) { UpdateValidation(); MarkDirty(); }
    partial void OnSelectedPlayer0Changed(Player? value) { UpdateValidation(); MarkDirty(); }
    partial void OnSelectedPlayer1Changed(Player? value) { UpdateValidation(); MarkDirty(); }
    partial void OnSelectedPlayer2Changed(Player? value) { UpdateValidation(); MarkDirty(); }
    partial void OnSelectedPlayer3Changed(Player? value) { UpdateValidation(); MarkDirty(); }

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

    [RelayCommand]
    private async Task CancelAsync()
    {
        if (!await ConfirmDiscardAsync().ConfigureAwait(true)) return;
        _context.Clear();
        await Shell.Current.GoToAsync("..").ConfigureAwait(true);
    }
}
