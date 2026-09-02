using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using DoubleDashScore.Data;
using DoubleDashScore.Services;

namespace DoubleDashScore.ViewModels;

[QueryProperty(nameof(NightId), "nightId")]
public partial class NightStatsViewModel : ObservableObject, IRecipient<DatabaseImportedMessage>
{
    private static readonly CultureInfo SvSe = CultureInfo.GetCultureInfo("sv-SE");

    // Dämpad separator som fungerar mot både ljus och mörk bakgrund (50 % grå).
    private static readonly Color SeparatorColor = Color.FromArgb("#80808080");

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
        WeakReferenceMessenger.Default.Register(this);
    }

    public void Receive(DatabaseImportedMessage message)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            try { await LoadAsync().ConfigureAwait(true); }
            catch { /* fire-and-forget */ }
        });
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

    /// <summary>Kvällens totalpoäng per spelare, bäst först, namnen i spelarfärg.</summary>
    [ObservableProperty]
    private FormattedString? _nightTotals;

    public async Task LoadAsync(CancellationToken ct = default)
    {
        if (NightId <= 0) return;
        IsBusy = true;
        try
        {
            Overview.Clear();
            RoundSections.Clear();
            NightTotals = null;
            HasData = false;
            StatusMessage = string.Empty;

            var night = await _nights.GetAsync(NightId, ct).ConfigureAwait(true);
            if (night is null)
            {
                Title = "Statistik (saknas)";
                StatusMessage = "Kvällen kunde inte hittas.";
                return;
            }
            Title = $"Statistik — {night.PlayedOn.ToLocalTime().ToString("d MMMM yyyy", SvSe)}";

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

            // Partiella omgångar visas också — deras poäng räknas redan in i
            // kvällssnittet, så att gömma dem gjorde bara vyn svårläst.
            foreach (var positions in stats.RoundPositions)
            {
                var rows = orderedIds.Select(id => new RoundPlayerRow(
                    nameById[id],
                    positions.PositionByPlayer[id].ToString(SvSe),
                    positions.TotalPointsByPlayer[id].ToString(SvSe))).ToList();
                var heading = positions.IsComplete
                    ? $"Omgång {positions.RoundNumber}"
                    : $"Omgång {positions.RoundNumber} (inkomplett)";
                RoundSections.Add(new RoundStatsSection(heading, rows));
            }

            NightTotals = BuildNightTotals(stats.TotalPointsByPlayer, nameById, orderedIds);

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

    // "Aleksi 79, Claes 62, ..." — bäst först, varje namn i sin spelarfärg.
    // Samma FormattedString-mönster som vinnarraden i NightsListViewModel:
    // en Label med färgade spans i stället för en layout per spelare.
    private static FormattedString BuildNightTotals(
        IReadOnlyDictionary<int, int> totalPointsByPlayer,
        IReadOnlyDictionary<int, string> nameById,
        IReadOnlyList<int> orderedIds)
    {
        var fs = new FormattedString();
        var ranked = orderedIds
            .OrderByDescending(id => totalPointsByPlayer[id])
            .ToList();

        for (int i = 0; i < ranked.Count; i++)
        {
            if (i > 0)
            {
                fs.Spans.Add(new Span { Text = ", ", TextColor = SeparatorColor });
            }
            var name = nameById[ranked[i]];
            var span = new Span { Text = $"{name} {totalPointsByPlayer[ranked[i]].ToString(SvSe)}" };
            // Okänt namn: lämna färgen osatt så tema-defaulten gäller.
            if (PlayerColors.MauiColorFor(name) is { } color) span.TextColor = color;
            fs.Spans.Add(span);
        }

        return fs;
    }
}

public sealed record PlayerNightOverviewRow(string PlayerName, string AverageText, string PlacementsText);

public sealed record RoundPlayerRow(string PlayerName, string PositionText, string PointsText);

public sealed record RoundStatsSection(string Heading, IReadOnlyList<RoundPlayerRow> Rows);
