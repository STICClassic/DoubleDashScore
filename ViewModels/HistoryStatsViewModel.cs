using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DoubleDashScore.Data;
using DoubleDashScore.Services;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;

namespace DoubleDashScore.ViewModels;

public partial class HistoryStatsViewModel : ObservableObject
{
    private static readonly CultureInfo SvSe = CultureInfo.GetCultureInfo("sv-SE");

    // Färger matchar Excel-originalets uppdelning. Bundna till spelarens namn så
    // att en spelare alltid får sin egen färg oavsett DisplayOrder. Faller tillbaka
    // till positionell färgmatchning om namnet är okänt.
    //
    // Valda mot ljusgrå plot-bakgrund (#E8E8E8). Jonas är medvetet mörkare än
    // ren gul (#FFD700) eftersom ren gul har kontrast ~1.1:1 mot #E8E8E8 och blir
    // i princip osynlig — #B8860B (DarkGoldenrod) har ~3.0:1 och läser
    // fortfarande som gul/guld.
    private static readonly Dictionary<string, OxyColor> PlayerColorsByName =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Claes"]  = OxyColor.FromRgb(0xE5, 0x5A, 0x1F),  // röd-orange
            ["Robin"]  = OxyColor.FromRgb(0x1F, 0x77, 0xB4),  // blå
            ["Aleksi"] = OxyColor.FromRgb(0x2C, 0xA0, 0x2C),  // grön
            ["Jonas"]  = OxyColor.FromRgb(0xB8, 0x86, 0x0B),  // mörk gul/guld
        };

    private static readonly OxyColor[] FallbackPlayerColors =
    {
        OxyColor.FromRgb(0xE5, 0x5A, 0x1F),
        OxyColor.FromRgb(0x1F, 0x77, 0xB4),
        OxyColor.FromRgb(0x2C, 0xA0, 0x2C),
        OxyColor.FromRgb(0xB8, 0x86, 0x0B),
    };

    private static OxyColor ColorForPlayer(string playerName, int displayOrder)
    {
        return PlayerColorsByName.TryGetValue(playerName, out var c)
            ? c
            : FallbackPlayerColors[displayOrder % FallbackPlayerColors.Length];
    }

    // Fast palett för plot-elementen (axlar, text, gridlines) — vi växlar inte
    // dark mode-färger längre eftersom bakgrunden är fast ljusgrå.
    private static readonly OxyColor ChartBackground = OxyColor.FromRgb(0xE8, 0xE8, 0xE8);
    private static readonly OxyColor ChartForeground = OxyColor.FromRgb(0x22, 0x22, 0x22);
    private static readonly OxyColor ChartGridline   = OxyColor.FromArgb(0x60, 0x00, 0x00, 0x00);
    private static readonly OxyColor ChartBorder     = OxyColor.FromArgb(0x8C, 0x00, 0x00, 0x00);

    private readonly GameNightRepository _nights;
    private readonly PlayerRepository _players;
    private readonly HistoricalDataRepository _historical;
    private readonly ChartTransferStore _chartStore;

    public HistoryStatsViewModel(
        GameNightRepository nights,
        PlayerRepository players,
        HistoricalDataRepository historical,
        ChartTransferStore chartStore)
    {
        _nights = nights;
        _players = players;
        _historical = historical;
        _chartStore = chartStore;
    }

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _hasData;

    [ObservableProperty]
    private PlotModel? _plotModel;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTotalsTab))]
    [NotifyPropertyChangedFor(nameof(IsGraphTab))]
    [NotifyPropertyChangedFor(nameof(IsPlacementsTab))]
    private int _selectedTabIndex;

    public bool IsTotalsTab => SelectedTabIndex == 0;
    public bool IsGraphTab => SelectedTabIndex == 1;
    public bool IsPlacementsTab => SelectedTabIndex == 2;

    [ObservableProperty]
    private PlacementHeaders _placementsHeaders = new(string.Empty, string.Empty, string.Empty, string.Empty);

    public ObservableCollection<TotalsRow> Totals { get; } = new();

    public ObservableCollection<PlacementsRow> PlacementsRows { get; } = new();

    [RelayCommand]
    private void SelectTab(string indexText)
    {
        if (int.TryParse(indexText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var idx))
        {
            SelectedTabIndex = idx;
        }
    }

    [RelayCommand]
    private static async Task OpenFullScreenChart()
    {
        await Shell.Current.GoToAsync("FullScreenChartPage").ConfigureAwait(true);
    }

    public async Task LoadAsync(CancellationToken ct = default)
    {
        IsBusy = true;
        try
        {
            Totals.Clear();
            PlacementsRows.Clear();
            HasData = false;
            StatusMessage = string.Empty;
            PlotModel = null;

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
            foreach (var point in stats.Series)
            {
                PlacementsRows.Add(new PlacementsRow(
                    BuildNightLabel(point),
                    FormatPlacements(point, orderedIds[0]),
                    FormatPlacements(point, orderedIds[1]),
                    FormatPlacements(point, orderedIds[2]),
                    FormatPlacements(point, orderedIds[3])));
            }

            PlotModel = BuildPlotModel(stats.Series, orderedIds, nameById);
            _chartStore.CurrentPlotModel = PlotModel;
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

    private static PlotModel BuildPlotModel(
        IReadOnlyList<NightAveragePoint> series,
        IReadOnlyList<int> orderedIds,
        IReadOnlyDictionary<int, string> nameById)
    {
        var model = new PlotModel
        {
            Title = "Kvällssnitt över tid",
            TitleFontSize = 14,
            TitleColor = ChartForeground,
            TextColor = ChartForeground,
            PlotAreaBorderColor = ChartBorder,
            Background = ChartBackground,
            PlotAreaBackground = ChartBackground,
            Culture = SvSe,
        };

        var nightCount = series.Count;
        var xAxis = new LinearAxis
        {
            Position = AxisPosition.Bottom,
            Minimum = 0.5,
            Maximum = nightCount + 0.5,
            // Etiketter bara var 10:e kväll så att 91+ punkter inte fyller axeln helt.
            MajorStep = 10,
            MinorStep = 1,
            LabelFormatter = v =>
            {
                var n = (int)Math.Round(v);
                return Math.Abs(v - n) < 0.0001 && n >= 1 && n <= nightCount
                    ? n.ToString(SvSe)
                    : string.Empty;
            },
            MajorGridlineStyle = LineStyle.Dot,
            MajorGridlineColor = ChartGridline,
            MinorTickSize = 0,
            TextColor = ChartForeground,
            TicklineColor = ChartForeground,
            AxislineColor = ChartForeground,
            TitleColor = ChartForeground,
        };
        var yAxis = new LinearAxis
        {
            Position = AxisPosition.Left,
            Minimum = 1,
            Maximum = 4,
            MajorStep = 1,
            MinorStep = 0.5,
            MajorGridlineStyle = LineStyle.Dot,
            MajorGridlineColor = ChartGridline,
            StringFormat = "0.00",
            TextColor = ChartForeground,
            TicklineColor = ChartForeground,
            AxislineColor = ChartForeground,
            TitleColor = ChartForeground,
        };
        model.Axes.Add(xAxis);
        model.Axes.Add(yAxis);

        var pointsByPlayer = orderedIds.ToDictionary(id => id, _ => new List<NightSeriesPoint>());
        foreach (var point in series)
        {
            var header = BuildTooltipHeader(point);
            foreach (var id in orderedIds)
            {
                if (!point.AverageByPlayer.TryGetValue(id, out var avg)) continue;
                pointsByPlayer[id].Add(new NightSeriesPoint(
                    point.ChronologicalIndex,
                    (double)avg,
                    header));
            }
        }

        for (int i = 0; i < orderedIds.Count; i++)
        {
            var id = orderedIds[i];
            var items = pointsByPlayer[id];
            if (items.Count == 0) continue;

            var name = nameById[id];
            var color = ColorForPlayer(name, i);
            var line = new LineSeries
            {
                Title = name,
                ItemsSource = items,
                DataFieldX = nameof(NightSeriesPoint.NightNumber),
                DataFieldY = nameof(NightSeriesPoint.Average),
                // Inga cirkelmarkörer — med 91+ punkter blir det helt grötigt.
                // Tracker fungerar fortfarande utan synliga markörer.
                MarkerType = MarkerType.None,
                StrokeThickness = 2,
                Color = color,
                TrackerFormatString = "{0}\n{Header}\nKvällssnitt: {Average:0.00}",
            };
            model.Series.Add(line);
        }

        model.Legends.Add(new OxyPlot.Legends.Legend
        {
            LegendPlacement = OxyPlot.Legends.LegendPlacement.Outside,
            LegendPosition = OxyPlot.Legends.LegendPosition.BottomCenter,
            LegendOrientation = OxyPlot.Legends.LegendOrientation.Horizontal,
            LegendTextColor = ChartForeground,
        });

        return model;
    }

    private static string BuildTooltipHeader(NightAveragePoint point)
    {
        if (point.HistoricalNightNumber is { } histNumber)
        {
            return $"Kväll {histNumber.ToString(SvSe)}";
        }
        if (point.PlayedOnUtc is { } playedOn)
        {
            return $"Kväll {point.ChronologicalIndex.ToString(SvSe)} — {playedOn.ToLocalTime():yyyy-MM-dd}";
        }
        throw new InvalidOperationException(
            $"Kvällspunkt {point.ChronologicalIndex} saknar både HistoricalNightNumber och PlayedOnUtc — datakorruption misstänks.");
    }

    private static string BuildNightLabel(NightAveragePoint point)
    {
        // Placerings-tabellen vill ha kort etikett ("Kväll 35"). Använd historiens
        // egna nummer när det finns, annars unified-index för app-kvällar.
        var n = point.HistoricalNightNumber ?? point.ChronologicalIndex;
        return $"Kväll {n.ToString(SvSe)}";
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

public sealed record TotalsRow(
    string PlayerName,
    string Firsts,
    string Seconds,
    string Thirds,
    string Fourths,
    string CareerAverage);

public sealed record NightSeriesPoint(int NightNumber, double Average, string Header);

public sealed record PlacementsRow(
    string NightLabel,
    string Player1,
    string Player2,
    string Player3,
    string Player4);

public sealed record PlacementHeaders(
    string Player1,
    string Player2,
    string Player3,
    string Player4);
