using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
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
        OxyColor.FromRgb(0x1F, 0x77, 0xB4),
        OxyColor.FromRgb(0xD6, 0x27, 0x28),
        OxyColor.FromRgb(0x2C, 0xA0, 0x2C),
        OxyColor.FromRgb(0xFF, 0x7F, 0x0E),
    };

    private readonly GameNightRepository _nights;
    private readonly PlayerRepository _players;

    public HistoryStatsViewModel(GameNightRepository nights, PlayerRepository players)
    {
        _nights = nights;
        _players = players;
    }

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _hasData;

    [ObservableProperty]
    private PlotModel? _plotModel;

    public ObservableCollection<TotalsRow> Totals { get; } = new();

    public async Task LoadAsync(CancellationToken ct = default)
    {
        IsBusy = true;
        try
        {
            Totals.Clear();
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
            if (withRounds.Count == 0)
            {
                StatusMessage = "Inga kvällar med omgångar än.";
                return;
            }

            var stats = StatsCalculator.CalculateHistory(withRounds, orderedIds);

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

            PlotModel = BuildPlotModel(stats.Series, orderedIds, nameById);
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
            PlotAreaBorderColor = OxyColor.FromArgb(80, 0, 0, 0),
            Culture = SvSe,
        };

        var dateAxis = new DateTimeAxis
        {
            Position = AxisPosition.Bottom,
            StringFormat = "yyyy-MM-dd",
            IntervalType = DateTimeIntervalType.Auto,
            MajorGridlineStyle = LineStyle.Dot,
            MajorGridlineColor = OxyColor.FromArgb(40, 0, 0, 0),
            Angle = -45,
        };
        var valueAxis = new LinearAxis
        {
            Position = AxisPosition.Left,
            Minimum = 1,
            Maximum = 4,
            MajorStep = 1,
            MinorStep = 0.5,
            MajorGridlineStyle = LineStyle.Dot,
            MajorGridlineColor = OxyColor.FromArgb(40, 0, 0, 0),
            StringFormat = "0.00",
        };
        model.Axes.Add(dateAxis);
        model.Axes.Add(valueAxis);

        for (int i = 0; i < orderedIds.Count; i++)
        {
            var id = orderedIds[i];
            var line = new LineSeries
            {
                Title = nameById[id],
                MarkerType = MarkerType.Circle,
                MarkerSize = 4,
                StrokeThickness = 2,
                Color = PlayerColors[i % PlayerColors.Length],
                MarkerFill = PlayerColors[i % PlayerColors.Length],
                TrackerFormatString = "{0}\n{1}: {2:yyyy-MM-dd}\n{3}: {4:0.00}",
            };
            foreach (var point in series)
            {
                if (!point.AverageByPlayer.TryGetValue(id, out var avg)) continue;
                line.Points.Add(new DataPoint(
                    DateTimeAxis.ToDouble(point.PlayedOnUtc.ToLocalTime()),
                    (double)avg));
            }
            if (line.Points.Count > 0)
            {
                model.Series.Add(line);
            }
        }

        model.Legends.Add(new OxyPlot.Legends.Legend
        {
            LegendPlacement = OxyPlot.Legends.LegendPlacement.Outside,
            LegendPosition = OxyPlot.Legends.LegendPosition.BottomCenter,
            LegendOrientation = OxyPlot.Legends.LegendOrientation.Horizontal,
        });

        return model;
    }
}

public sealed record TotalsRow(
    string PlayerName,
    string Firsts,
    string Seconds,
    string Thirds,
    string Fourths,
    string CareerAverage);
