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
    private readonly PlayerRepository _players;
    private readonly RoundRepository _rounds;

    public RoundEntryViewModel(PlayerRepository players, RoundRepository rounds)
    {
        _players = players;
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

    public List<PlayerColumnViewModel> Players { get; } = new();

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
        try
        {
            DetachColumnHandlers();
            Players.Clear();

            var activePlayers = await _players.GetActivePlayersAsync(ct).ConfigureAwait(true);
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
                Players.Add(col);
            }

            UpdateValidation();
        }
        finally
        {
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
        UpdateValidation();
    }

    partial void OnTrackCountTextChanged(string value)
    {
        UpdateValidation();
    }

    private void UpdateValidation()
    {
        var (_, message) = RoundMatrixValidator.Validate(Players, TrackCountText);
        ValidationMessage = message;
        OnPropertyChanged(nameof(IsValid));
        SaveCommand.NotifyCanExecuteChanged();
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
            await Shell.Current.GoToAsync("..").ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private static async Task CancelAsync()
    {
        await Shell.Current.GoToAsync("..").ConfigureAwait(true);
    }
}
