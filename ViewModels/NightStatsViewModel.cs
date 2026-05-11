using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using DoubleDashScore.Data;
using DoubleDashScore.Services;

namespace DoubleDashScore.ViewModels;

[QueryProperty(nameof(NightId), "nightId")]
public partial class NightStatsViewModel : ObservableObject
{
    private static readonly CultureInfo SvSe = CultureInfo.GetCultureInfo("sv-SE");

    private readonly GameNightRepository _nights;
    private readonly RoundRepository _rounds;
    private readonly PlayerRepository _players;

    public NightStatsViewModel(
        GameNightRepository nights,
        RoundRepository rounds,
        PlayerRepository players)
    {
        _nights = nights;
        _rounds = rounds;
        _players = players;
    }

    [ObservableProperty]
    private int _nightId;

    [ObservableProperty]
    private string _title = "Statistik";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _hasData;

    public ObservableCollection<PlayerNightOverviewRow> Overview { get; } = new();
    public ObservableCollection<RoundStatsSection> RoundSections { get; } = new();

    public async Task LoadAsync(CancellationToken ct = default)
    {
        if (NightId <= 0) return;
        IsBusy = true;
        try
        {
            Overview.Clear();
            RoundSections.Clear();
            HasData = false;
            StatusMessage = string.Empty;

            var night = await _nights.GetAsync(NightId, ct).ConfigureAwait(true);
            if (night is null)
            {
                Title = "Statistik (saknas)";
                StatusMessage = "Kvällen kunde inte hittas.";
                return;
            }
            Title = $"Statistik — {night.PlayedOn.ToLocalTime():yyyy-MM-dd}";

            var activePlayers = await _players.GetActivePlayersAsync(ct).ConfigureAwait(true);
            if (activePlayers.Count != 4)
            {
                StatusMessage = $"Förväntade 4 aktiva spelare, hittade {activePlayers.Count}.";
                return;
            }
            var nameById = activePlayers.ToDictionary(p => p.Id, p => p.Name);
            var orderedIds = activePlayers.Select(p => p.Id).ToList();

            var roundDetails = await _rounds.GetRoundsForNightAsync(NightId, ct).ConfigureAwait(true);
            if (roundDetails.Count == 0)
            {
                StatusMessage = "Kvällen har inga omgångar än.";
                return;
            }

            var nightWith = new NightWithRounds(night, roundDetails);
            var stats = StatsCalculator.CalculateNightStats(nightWith, orderedIds);

            foreach (var id in orderedIds)
            {
                var placements = stats.PlacementsByPlayer[id];
                var placementsText = placements.Count == 0
                    ? "—"
                    : string.Join(", ", placements);
                Overview.Add(new PlayerNightOverviewRow(
                    nameById[id],
                    stats.AverageByPlayer[id].ToString("0.00", SvSe),
                    placementsText));
            }

            foreach (var positions in stats.CompleteRoundPositions)
            {
                var rows = orderedIds.Select(id => new RoundPlayerRow(
                    nameById[id],
                    positions.PositionByPlayer[id].ToString(SvSe),
                    positions.TotalPointsByPlayer[id].ToString(SvSe))).ToList();
                RoundSections.Add(new RoundStatsSection(
                    $"Omgång {positions.RoundNumber}",
                    rows));
            }

            if (RoundSections.Count == 0)
            {
                StatusMessage = "Inga kompletta omgångar än — per-omgång-tabellen visas när minst en omgång har 16 banor.";
            }

            HasData = true;
        }
        catch (InvalidOperationException ex)
        {
            StatusMessage = $"Kan inte beräkna statistik: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}

public sealed record PlayerNightOverviewRow(string PlayerName, string AverageText, string PlacementsText);

public sealed record RoundPlayerRow(string PlayerName, string PositionText, string PointsText);

public sealed record RoundStatsSection(string Heading, IReadOnlyList<RoundPlayerRow> Rows);
