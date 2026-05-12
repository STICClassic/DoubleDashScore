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

    private static readonly OxyColor[] PlayerColors =
    {
        OxyColor.FromRgb(0x4E, 0x9C, 0xFF),
        OxyColor.FromRgb(0xE6, 0x46, 0x46),
        OxyColor.FromRgb(0x4C, 0xAF, 0x50),
        OxyColor.FromRgb(0xFF, 0x98, 0x00),
    };

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
        var theme = GetThemeColors();

        var model = new PlotModel
        {
            Title = "Kvällssnitt över tid",
            TitleFontSize = 14,
            TitleColor = theme.Foreground,
            TextColor = theme.Foreground,
            PlotAreaBorderColor = theme.Border,
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
            MajorGridlineColor = theme.Gridline,
            MinorTickSize = 0,
            TextColor = theme.Foreground,
            TicklineColor = theme.Foreground,
            AxislineColor = theme.Foreground,
            TitleColor = theme.Foreground,
        };
        var yAxis = new LinearAxis
        {
            Position = AxisPosition.Left,
            Minimum = 1,
            Maximum = 4,
            MajorStep = 1,
            MinorStep = 0.5,
            MajorGridlineStyle = LineStyle.Dot,
            MajorGridlineColor = theme.Gridline,
            StringFormat = "0.00",
            TextColor = theme.Foreground,
            TicklineColor = theme.Foreground,
            AxislineColor = theme.Foreground,
            TitleColor = theme.Foreground,
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

            var color = PlayerColors[i % PlayerColors.Length];
            var line = new LineSeries
            {
                Title = nameById[id],
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
            LegendTextColor = theme.Foreground,
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

    private static ThemeColors GetThemeColors()
    {
        var theme = Application.Current?.RequestedTheme ?? AppTheme.Light;
        if (theme == AppTheme.Unspecified) theme = AppTheme.Light;
        return theme == AppTheme.Dark
            ? new ThemeColors(
                Foreground: OxyColor.FromRgb(0xE6, 0xE6, 0xE6),
                Gridline: OxyColor.FromArgb(80, 0xCC, 0xCC, 0xCC),
                Border: OxyColor.FromArgb(160, 0xCC, 0xCC, 0xCC))
            : new ThemeColors(
                Foreground: OxyColor.FromRgb(0x22, 0x22, 0x22),
                Gridline: OxyColor.FromArgb(60, 0x00, 0x00, 0x00),
                Border: OxyColor.FromArgb(140, 0x00, 0x00, 0x00));
    }

    private sealed record ThemeColors(OxyColor Foreground, OxyColor Gridline, OxyColor Border);
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
