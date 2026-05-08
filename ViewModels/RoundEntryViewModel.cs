using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DoubleDashScore.Data;

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

    public bool IsValid => ComputeValidation(out _);

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
        ComputeValidation(out var message);
        ValidationMessage = message;
        OnPropertyChanged(nameof(IsValid));
        SaveCommand.NotifyCanExecuteChanged();
    }

    private bool ComputeValidation(out string message)
    {
        message = string.Empty;
        var trackCount = TrackCountValue;
        if (trackCount < 1 || trackCount > 16)
        {
            message = "Antal banor måste vara mellan 1 och 16.";
            return false;
        }
        if (Players.Count != 4)
        {
            message = "Fyra spelare krävs.";
            return false;
        }
        var problems = new List<string>();
        for (int i = 0; i < Players.Count; i++)
        {
            var p = Players[i];
            if (!p.TryGetCounts(out var c))
            {
                problems.Add($"{p.PlayerName}: ogiltig siffra.");
                continue;
            }
            var sum = c.first + c.second + c.third + c.fourth;
            if (sum != trackCount)
            {
                problems.Add($"{p.PlayerName}: {sum}/{trackCount}.");
            }
        }
        var (firstSum, secondSum, thirdSum, fourthSum) = SumByPosition();
        if (firstSum != trackCount) problems.Add($"1:or totalt {firstSum}/{trackCount}.");
        if (secondSum != trackCount) problems.Add($"2:or totalt {secondSum}/{trackCount}.");
        if (thirdSum != trackCount) problems.Add($"3:or totalt {thirdSum}/{trackCount}.");
        if (fourthSum != trackCount) problems.Add($"4:or totalt {fourthSum}/{trackCount}.");

        if (problems.Count == 0)
        {
            message = "Klar att spara.";
            return true;
        }
        message = string.Join("  ", problems);
        return false;
    }

    private (int first, int second, int third, int fourth) SumByPosition()
    {
        int f = 0, s = 0, t = 0, fo = 0;
        foreach (var p in Players)
        {
            if (!p.TryGetCounts(out var c)) continue;
            f += c.first;
            s += c.second;
            t += c.third;
            fo += c.fourth;
        }
        return (f, s, t, fo);
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

public partial class PlayerColumnViewModel : ObservableObject
{
    public PlayerColumnViewModel(int playerId, string playerName)
    {
        PlayerId = playerId;
        PlayerName = playerName;
    }

    public int PlayerId { get; }
    public string PlayerName { get; }

    [ObservableProperty]
    private string _firstPlacesText = "0";

    [ObservableProperty]
    private string _secondPlacesText = "0";

    [ObservableProperty]
    private string _thirdPlacesText = "0";

    [ObservableProperty]
    private string _fourthPlacesText = "0";

    public bool TryGetCounts(out (int first, int second, int third, int fourth) counts)
    {
        counts = default;
        if (!TryParseNonNegative(FirstPlacesText, out var first)) return false;
        if (!TryParseNonNegative(SecondPlacesText, out var second)) return false;
        if (!TryParseNonNegative(ThirdPlacesText, out var third)) return false;
        if (!TryParseNonNegative(FourthPlacesText, out var fourth)) return false;
        counts = (first, second, third, fourth);
        return true;
    }

    private static bool TryParseNonNegative(string? text, out int value)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            value = 0;
            return true;
        }
        if (int.TryParse(text, out value) && value >= 0)
        {
            return true;
        }
        value = 0;
        return false;
    }
}
