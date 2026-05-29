using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using DoubleDashScore.Data;
using DoubleDashScore.Services;

namespace DoubleDashScore.ViewModels;

// Visar Totalscore + senaste 4 kvällarnas placeringar på en skärm utan
// scroll. Återanvänder TotalscoreTable + NightPlacementsBlock-komponenterna
// så att Statistik-tabbarnas rendering matchar exakt. Avsikten är att
// användaren själv tar en skärmdump på telefonen för att dela vidare —
// ingen dela-funktion byggs in (medvetet val Skiva 18).
//
// Skiljer sig från HistoryStatsViewModel.PlacementsRows på en punkt:
// label-formatet är sv-SE-datum för app-recorded kvällar i stället för
// "Kväll N" som Placeringar-tabben använder. Historiska seed-kvällar
// saknar datum och faller då tillbaka på "Kväll N".
public partial class OversiktViewModel : ObservableObject, IRecipient<DatabaseImportedMessage>
{
    private static readonly CultureInfo SvSe = CultureInfo.GetCultureInfo("sv-SE");

    private readonly GameNightRepository _nights;
    private readonly PlayerRepository _players;
    private readonly HistoricalDataRepository _historical;

    public OversiktViewModel(
        GameNightRepository nights,
        PlayerRepository players,
        HistoricalDataRepository historical)
    {
        _nights = nights;
        _players = players;
        _historical = historical;
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
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _hasData;

    // Karriärsnitt:s synlighet är lokal till denna vy — användaren får
    // toggla på Översikt utan att Totalscore-tabbens state ändras (varje
    // sidvisning börjar med dolt karriärsnitt, samma policy som HistoryStats).
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CareerAverageToggleLabel))]
    private bool _isCareerAverageVisible;

    public string CareerAverageToggleLabel =>
        IsCareerAverageVisible ? "Dölj karriärsnitt" : "Visa karriärsnitt";

    [RelayCommand]
    private void ToggleCareerAverage()
    {
        IsCareerAverageVisible = !IsCareerAverageVisible;
    }

    [ObservableProperty]
    private PlacementHeaders _placementsHeaders =
        new(string.Empty, string.Empty, string.Empty, string.Empty);

    public ObservableCollection<TotalsRow> Totals { get; } = new();

    public ObservableCollection<PlacementsRow> RecentNights { get; } = new();

    public async Task LoadAsync(CancellationToken ct = default)
    {
        IsBusy = true;
        try
        {
            Totals.Clear();
            RecentNights.Clear();
            HasData = false;
            StatusMessage = string.Empty;

            var activePlayers = await _players.GetActivePlayersAsync(ct).ConfigureAwait(true);
            if (activePlayers.Count != 4)
            {
                StatusMessage = $"Förväntade 4 aktiva spelare, hittade {activePlayers.Count}.";
                return;
            }
            var nameById = activePlayers.ToDictionary(p => p.Id, p => p.Name);
            var orderedIds = activePlayers.Select(p => p.Id).ToList();

            var nights = await _nights.GetAllNightsWithRoundsAsync(ct).ConfigureAwait(true);
            var withRounds = nights.Where(n => n.Rounds.Count > 0).ToList();
            var seed = await _historical.GetSeedAsync(ct).ConfigureAwait(true);
            if (withRounds.Count == 0 && seed.IsEmpty)
            {
                StatusMessage = "Inga kvällar med omgångar än.";
                return;
            }

            var stats = StatsCalculator.CalculateHistory(withRounds, orderedIds, seed);

            // Totals — exakt samma logik som HistoryStatsViewModel.LoadAsync.
            // Källan (stats.PositionTotals + CareerAverageByPlayer) är samma så
            // siffrorna matchar Totalscore-tabben till sista decimalen.
            foreach (var id in orderedIds)
            {
                var counts = stats.PositionTotals.ByPlayer[id];
                var career = stats.CareerAverageByPlayer[id];
                Totals.Add(new TotalsRow(
                    nameById[id],
                    counts.Firsts.ToString(SvSe),
                    counts.Seconds.ToString(SvSe),
                    counts.Thirds.ToString(SvSe),
                    counts.Fourths.ToString(SvSe),
                    career.ToString("0.00", SvSe)));
            }

            PlacementsHeaders = new PlacementHeaders(
                nameById[orderedIds[0]],
                nameById[orderedIds[1]],
                nameById[orderedIds[2]],
                nameById[orderedIds[3]]);

            // De 4 senaste kvällarna = sista 4 i stats.Series (kronologisk
            // ordning, historiska först, sen app-kvällar). TakeLast hanterar
            // gracefully om vi har < 4 kvällar (krasch-fritt edge case).
            var recent = stats.Series.TakeLast(4);
            foreach (var point in recent)
            {
                RecentNights.Add(new PlacementsRow(
                    BuildOversiktLabel(point),
                    FormatPlacements(point, orderedIds[0]),
                    FormatPlacements(point, orderedIds[1]),
                    FormatPlacements(point, orderedIds[2]),
                    FormatPlacements(point, orderedIds[3])));
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

    // sv-SE-datum för app-recorded kvällar ("18 maj 2026"); "Kväll N" för
    // historiska seed-kvällar som saknar datum. Avviker från HistoryStats-
    // ViewModel.BuildNightLabel som alltid returnerar "Kväll N" — Översikten
    // är en delningsbar sammanfattning där läsbara datum är viktigare än
    // numreringen.
    private static string BuildOversiktLabel(NightAveragePoint point)
    {
        if (point.PlayedOnUtc is { } playedOn)
        {
            return playedOn.ToLocalTime().ToString("d MMMM yyyy", SvSe);
        }
        if (point.HistoricalNightNumber is { } histNumber)
        {
            return $"Kväll {histNumber.ToString(SvSe)}";
        }
        throw new InvalidOperationException(
            $"Kvällspunkt {point.ChronologicalIndex} saknar både HistoricalNightNumber och PlayedOnUtc — datakorruption misstänks.");
    }

    private static string FormatPlacements(NightAveragePoint point, int playerId)
    {
        if (!point.PlacementsByPlayer.TryGetValue(playerId, out var list) || list.Count == 0)
        {
            return string.Empty;
        }
        return string.Join(", ", list.Select(p => p.IsTied
            ? $"{p.Position.ToString(SvSe)}*"
            : p.Position.ToString(SvSe)));
    }
}
