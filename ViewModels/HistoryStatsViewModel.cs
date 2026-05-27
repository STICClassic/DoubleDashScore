using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using DoubleDashScore.Data;
using DoubleDashScore.Services;
using Microsoft.Maui.Graphics;
using OxyPlot;
using OxyPlot.Annotations;
using OxyPlot.Axes;
using OxyPlot.Series;

namespace DoubleDashScore.ViewModels;

public partial class HistoryStatsViewModel : ObservableObject, IRecipient<DatabaseImportedMessage>
{
    private static readonly CultureInfo SvSe = CultureInfo.GetCultureInfo("sv-SE");

    // Färger matchar Excel-originalets uppdelning. Bundna till spelarens namn så
    // att en spelare alltid får sin egen färg oavsett DisplayOrder. Faller tillbaka
    // till positionell färgmatchning om namnet är okänt.
    //
    // Valda mot grå plot-bakgrund (#C8C8C8). Jonas är medvetet mörkare än
    // ren gul (#FFD700) eftersom ren gul har dålig kontrast mot grått och blir
    // i princip osynlig — #B8860B (DarkGoldenrod) läser fortfarande som gul/guld
    // och har god kontrast även mot mörkare grått.
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

    // Fast palett för plot-elementen (axlar, text, border) — vi växlar inte
    // dark mode-färger längre eftersom bakgrunden är fast grå.
    private static readonly OxyColor ChartBackground = OxyColor.FromRgb(0xC8, 0xC8, 0xC8);
    private static readonly OxyColor ChartForeground = OxyColor.FromRgb(0x22, 0x22, 0x22);
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
        WeakReferenceMessenger.Default.Register(this);
    }

    // Reload när användaren importerar en .db-backup, oavsett vilken tabb som
    // är aktiv just nu (LoadAsync återskapar Totals, PlacementsRows och PlotModel).
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

    [ObservableProperty]
    private PlotModel? _plotModel;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTotalsTab))]
    [NotifyPropertyChangedFor(nameof(IsGraphTab))]
    [NotifyPropertyChangedFor(nameof(IsPlacementsTab))]
    private int _selectedTabIndex;

    public bool IsTotalsTab => SelectedTabIndex == 0;
    public bool IsPlacementsTab => SelectedTabIndex == 1;
    public bool IsGraphTab => SelectedTabIndex == 2;

    // Karriärsnitt:et är dolt som default — användaren vill kunna se sitt
    // eget snitt privat men inte ha det synligt by default. Togglas via
    // en text-knapp ovanför listan; ingen persistering, varje app-session
    // börjar med dolt.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CareerAverageToggleLabel))]
    private bool _isCareerAverageVisible;

    // Text för toggle-knappen ovanför Totalscore-listan.
    public string CareerAverageToggleLabel =>
        IsCareerAverageVisible ? "Dölj karriärsnitt" : "Visa karriärsnitt";

    [RelayCommand]
    private void ToggleCareerAverage()
    {
        IsCareerAverageVisible = !IsCareerAverageVisible;
    }

    [ObservableProperty]
    private PlacementHeaders _placementsHeaders = new(string.Empty, string.Empty, string.Empty, string.Empty);

    public ObservableCollection<TotalsRow> Totals { get; } = new();

    public ObservableCollection<PlacementsRow> PlacementsRows { get; } = new();

    // Egen legend under grafen — OxyPlots inbyggda legend exponerar inte
    // tap-events i MAUI på något användbart sätt. Items synkar IsVisible
    // mot LineSeries.IsVisible och ChartTransferStore.HiddenPlayerNames.
    public ObservableCollection<PlayerLegendItem> LegendItems { get; } = new();

    // ----- Scrubber / vald-kväll-panel -------------------------------------

    // Alla kvällar i kronologisk ordning (historiska först, sen app-kvällar).
    // SelectedNightIndex är ett index i denna lista. Återbyggs varje LoadAsync.
    private List<NightScrubberSlice> _allSlices = new();

    // Vertikal markörlinje på grafen vid vald kvälls ChronologicalIndex.
    // Tilldelas till nuvarande PlotModel:s Annotations. PlotModel:en byggs om
    // i LoadAsync — då nollas referensen och annotation:en återskapas mot
    // den nya modellen.
    private LineAnnotation? _scrubberAnnotation;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsScrubberSliderEnabled))]
    private int _scrubberMaximum;

    public bool IsScrubberSliderEnabled => ScrubberMaximum > 0;

    [ObservableProperty]
    private double _scrubberValue;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedNightSlice))]
    [NotifyPropertyChangedFor(nameof(HasSelectedNightSlice))]
    private int _selectedNightIndex = -1;

    public NightScrubberSlice? SelectedNightSlice =>
        SelectedNightIndex >= 0 && SelectedNightIndex < _allSlices.Count
            ? _allSlices[SelectedNightIndex]
            : null;

    public bool HasSelectedNightSlice => SelectedNightSlice is not null;

    // Slidern är double-bunden (MAUI Slider) men vi snäppar till heltal —
    // panel och annotation refererar bara till hela kvällar.
    partial void OnScrubberValueChanged(double value)
    {
        var idx = (int)Math.Round(value);
        if (idx < 0 || idx > ScrubberMaximum) return;
        if (idx == SelectedNightIndex) return;
        SelectedNightIndex = idx;
    }

    partial void OnSelectedNightIndexChanged(int value)
    {
        UpdateScrubberAnnotation();
    }

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
            // Applicera tidigare avvalda spelare (kan ha togglats i fullscreen
            // innan användaren navigerade tillbaka, eller från en tidigare
            // session sett över datarefreshes). Påverkar series.IsVisible
            // innan första rendering så grafen inte blinkar.
            _chartStore.ApplyVisibilityToPlot();
            RebuildLegendItems(PlotModel, orderedIds, nameById);

            // Scrubber: bygg slice-listan + sätt default-vald kväll till senaste.
            _allSlices = BuildScrubberSlices(stats.Series, orderedIds, nameById);
            ScrubberMaximum = Math.Max(_allSlices.Count - 1, 0);
            // PlotModel är ny — släpp gamla annotation-referensen, UpdateScrubberAnnotation
            // skapar en ny på nuvarande modell.
            _scrubberAnnotation = null;
            var defaultIdx = _allSlices.Count > 0 ? _allSlices.Count - 1 : -1;
            ScrubberValue = defaultIdx >= 0 ? defaultIdx : 0;
            // Sätt index efter ScrubberValue så OnScrubberValueChanged inte slår
            // tillbaka och blockerar (samma värde → ignored).
            SelectedNightIndex = defaultIdx;
            // Belt-and-braces: om idx var samma som förra LoadAsync fired ingen
            // partial — applicera annotation explicit mot nya PlotModel:en.
            UpdateScrubberAnnotation();
            OnPropertyChanged(nameof(SelectedNightSlice));
            OnPropertyChanged(nameof(HasSelectedNightSlice));

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
            MajorGridlineStyle = LineStyle.None,
            MinorGridlineStyle = LineStyle.None,
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
            MajorGridlineStyle = LineStyle.None,
            MinorGridlineStyle = LineStyle.None,
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

        // Ingen OxyPlot-inbyggd legend — vi renderar en egen tap:bar legend
        // under grafen i XAML (HistoryStatsPage + FullScreenChartPage).

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
            return $"Kväll {point.ChronologicalIndex.ToString(SvSe)} — {playedOn.ToLocalTime().ToString("d MMMM yyyy", SvSe)}";
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

    private void RebuildLegendItems(
        PlotModel model,
        IReadOnlyList<int> orderedIds,
        IReadOnlyDictionary<int, string> nameById)
    {
        LegendItems.Clear();
        for (int i = 0; i < orderedIds.Count; i++)
        {
            var name = nameById[orderedIds[i]];
            // Hämta serie för spelaren — kan saknas om StatsCalculator inte
            // levererade några punkter för hen, men det är osannolikt med
            // 4-spelar-invarianten. Skippar då legend-raden.
            var series = model.Series.FirstOrDefault(s => string.Equals(s.Title, name, StringComparison.Ordinal));
            if (series is null) continue;

            var oxy = ColorForPlayer(name, i);
            var mauiColor = Microsoft.Maui.Graphics.Color.FromRgba(oxy.R, oxy.G, oxy.B, oxy.A);
            LegendItems.Add(new PlayerLegendItem(name, mauiColor, series.IsVisible));
        }
    }

    private static List<NightScrubberSlice> BuildScrubberSlices(
        IReadOnlyList<NightAveragePoint> series,
        IReadOnlyList<int> orderedIds,
        IReadOnlyDictionary<int, string> nameById)
    {
        var slices = new List<NightScrubberSlice>(series.Count);
        foreach (var point in series)
        {
            var rows = new List<PlayerNightAverageRow>(4);
            for (int i = 0; i < orderedIds.Count; i++)
            {
                var id = orderedIds[i];
                if (!point.AverageByPlayer.TryGetValue(id, out var avg))
                {
                    continue;
                }
                var name = nameById[id];
                var oxy = ColorForPlayer(name, i);
                var color = Color.FromRgba(oxy.R, oxy.G, oxy.B, oxy.A);
                rows.Add(new PlayerNightAverageRow(
                    name,
                    avg,
                    avg.ToString("0.00", SvSe),
                    color));
            }
            // Bäst → sämst: högt banpoäng-snitt = bra placering (4=alltid 1:a).
            rows.Sort((a, b) => b.Average.CompareTo(a.Average));

            slices.Add(new NightScrubberSlice(
                point.ChronologicalIndex,
                BuildTooltipHeader(point),
                rows));
        }
        return slices;
    }

    private void UpdateScrubberAnnotation()
    {
        if (PlotModel is null) return;

        // Om PlotModel byggts om sedan annotation:en skapades — kasta gamla
        // referensen så vi inte försöker peka i en stale modell.
        if (_scrubberAnnotation is not null && !PlotModel.Annotations.Contains(_scrubberAnnotation))
        {
            _scrubberAnnotation = null;
        }

        if (SelectedNightIndex < 0 || SelectedNightIndex >= _allSlices.Count)
        {
            if (_scrubberAnnotation is not null)
            {
                PlotModel.Annotations.Remove(_scrubberAnnotation);
                _scrubberAnnotation = null;
                PlotModel.InvalidatePlot(false);
            }
            return;
        }

        var slice = _allSlices[SelectedNightIndex];
        if (_scrubberAnnotation is null)
        {
            _scrubberAnnotation = new LineAnnotation
            {
                Type = LineAnnotationType.Vertical,
                Color = OxyColor.FromArgb(0xC0, 0x22, 0x22, 0x22),
                StrokeThickness = 2,
                LineStyle = LineStyle.Solid,
                ClipByYAxis = true,
            };
            PlotModel.Annotations.Add(_scrubberAnnotation);
        }
        _scrubberAnnotation.X = slice.ChronologicalIndex;
        PlotModel.InvalidatePlot(false);
    }

    [RelayCommand]
    private void TogglePlayerVisibility(PlayerLegendItem? item)
    {
        if (item is null || PlotModel is null) return;

        var nowVisible = _chartStore.TogglePlayerVisibility(item.Name);
        item.IsVisible = nowVisible;

        var series = PlotModel.Series.FirstOrDefault(s => string.Equals(s.Title, item.Name, StringComparison.Ordinal));
        if (series is null) return;
        series.IsVisible = nowVisible;
        // true → rebuild punkter också (vi behöver det här eftersom serien
        // kan dyka upp igen efter att ha varit dold och tracker-cachen kan
        // vara stale på vissa MAUI/OxyPlot-versioner).
        PlotModel.InvalidatePlot(true);
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

// En "skiva" i scrubbern — alla fyra spelares kvällssnitt för en given kväll,
// färdigsorterad bäst → sämst.
public sealed record NightScrubberSlice(
    int ChronologicalIndex,
    string DateLabel,
    IReadOnlyList<PlayerNightAverageRow> RowsBestToWorst);

public sealed record PlayerNightAverageRow(
    string PlayerName,
    decimal Average,
    string AverageDisplay,
    Color Color);
