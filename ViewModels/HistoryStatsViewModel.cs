using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using DoubleDashScore.Data;
using DoubleDashScore.Services;
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

    // Markörlinjens default-färg (samma alfa-grå som FullScreenChartViewModels
    // MarkerColorActive). I portrait-vyn är markören permanent synlig — det är
    // bara helskärm som auto-döljer den.
    private static readonly OxyColor MarkerColor = OxyColor.FromAColor(140, OxyColors.Gray);

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
    // är aktiv just nu (LoadAsync återskapar Totals, PlacementsRows och båda
    // PlotModels).
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

    // Kvällsgrafens PlotModel — varje kvälls eget kvällssnitt per spelare.
    [ObservableProperty]
    private PlotModel? _plotModel;

    // Karriärgrafens PlotModel — löpande karriärsnitt per spelare
    // (kumulativt medelvärde av kvällssnitten t.o.m. respektive kväll).
    // Tab-index 3, ny i Skiva 16.
    [ObservableProperty]
    private PlotModel? _careerPlotModel;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTotalsTab))]
    [NotifyPropertyChangedFor(nameof(IsPlacementsTab))]
    [NotifyPropertyChangedFor(nameof(IsNightGraphTab))]
    [NotifyPropertyChangedFor(nameof(IsCareerGraphTab))]
    private int _selectedTabIndex;

    public bool IsTotalsTab => SelectedTabIndex == 0;
    public bool IsPlacementsTab => SelectedTabIndex == 1;
    public bool IsNightGraphTab => SelectedTabIndex == 2;
    public bool IsCareerGraphTab => SelectedTabIndex == 3;

    // Karriärsnitt:et är dolt som default — användaren vill kunna se sitt
    // eget snitt privat men inte ha det synligt by default. Togglas via
    // en text-knapp ovanför listan; ingen persistering, varje app-session
    // börjar med dolt.
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
    private PlacementHeaders _placementsHeaders = new(string.Empty, string.Empty, string.Empty, string.Empty);

    public ObservableCollection<TotalsRow> Totals { get; } = new();

    public ObservableCollection<PlacementsRow> PlacementsRows { get; } = new();

    // Egna legender per graf-tab. Båda renderar samma fyra spelare med samma
    // färger, men NightAverage-värdena skiljer sig: kvällsgrafens legend visar
    // varje kvälls eget snitt, karriärgrafens visar det löpande karriärsnittet
    // vid samma kväll. Spelartoggle togglar synlighet i båda samtidigt.
    public ObservableCollection<PlayerLegendItem> LegendItems { get; } = new();
    public ObservableCollection<PlayerLegendItem> CareerLegendItems { get; } = new();

    // ----- Vald kväll (legenderna visar snitt + vertikal markörlinje) ------

    // Markörlinjerna ägs av ChartTransferStore.NightAverage.MarkerAnnotation
    // resp. CareerAverage.MarkerAnnotation — delas med FullScreenChartViewModel
    // för att undvika dubbel-annotation per modell (annars syns två
    // överlappande grå linjer + bara fullscreen:s kopia auto-döljs).

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedNightSlice))]
    [NotifyPropertyChangedFor(nameof(SelectedNightLabel))]
    [NotifyPropertyChangedFor(nameof(HasSelectedNight))]
    private int _selectedNightIndex = -1;

    // Slice för "Kväll N:"-etiketten i legenden. NightAverage-slot räcker —
    // DateLabel ("Kväll 35") är identisk i båda slots (samma kronologi);
    // skillnaden är bara värdena, som hanteras per-collection i ApplySelection.
    public NightScrubberSlice? SelectedNightSlice =>
        SelectedNightIndex >= 0 && SelectedNightIndex < _chartStore.NightAverage.NightSlices.Count
            ? _chartStore.NightAverage.NightSlices[SelectedNightIndex]
            : null;

    public string SelectedNightLabel => SelectedNightSlice?.DateLabel ?? string.Empty;

    public bool HasSelectedNight => SelectedNightSlice is not null;

    partial void OnSelectedNightIndexChanged(int value)
    {
        ApplySelection();
    }

    [RelayCommand]
    private void SelectTab(string indexText)
    {
        if (int.TryParse(indexText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var idx))
        {
            SelectedTabIndex = idx;
        }
    }

    // Två separata fullscreen-commands. Sätter ActiveGraph på store:n innan
    // navigering så FullScreenChartViewModel läser rätt slot. Tidigare static
    // OpenFullScreenChart kan inte längre vara static (måste röra _chartStore).
    [RelayCommand]
    private async Task OpenFullScreenNightChart()
    {
        _chartStore.ActiveGraph = GraphKind.NightAverage;
        await Shell.Current.GoToAsync("FullScreenChartPage").ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task OpenFullScreenCareerChart()
    {
        _chartStore.ActiveGraph = GraphKind.CareerAverage;
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
            CareerPlotModel = null;

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

            // Beräkna löpande karriärsnitt per spelare per kväll. Oviktat
            // medelvärde av kvällssnitten — inte points/tracks som CLAUDE.md:s
            // karriärsnitt-formel, eftersom historisk seed-data inte har
            // banantal per kväll. Oviktat är enda formeln som funkar för
            // både seed + live; matchar också användarens spec ("medelvärdet
            // av kvällssnitten").
            var cumulativeByChrono = BuildCumulativeCareerAverages(stats.Series, orderedIds);

            // Bygg båda PlotModels. valueSelector väljer vilken decimal som
            // hamnar på Y-axeln per (kväll, spelare).
            PlotModel = BuildPlotModel(
                title: "Kvällssnitt över tid",
                stats.Series,
                valueSelector: (point, playerId) =>
                    point.AverageByPlayer.TryGetValue(playerId, out var v) ? v : (decimal?)null,
                orderedIds,
                nameById);

            CareerPlotModel = BuildPlotModel(
                title: "Karriärsnitt över tid",
                stats.Series,
                valueSelector: (point, playerId) =>
                    cumulativeByChrono.TryGetValue(point.ChronologicalIndex, out var byPlayer)
                        && byPlayer.TryGetValue(playerId, out var v)
                        ? v
                        : (decimal?)null,
                orderedIds,
                nameById);

            // Lagra båda i store:n så fullscreen kan plocka rätt slot.
            _chartStore.NightAverage.PlotModel = PlotModel;
            _chartStore.CareerAverage.PlotModel = CareerPlotModel;
            _chartStore.NightAverage.NightSlices = BuildSlicesFromSeries(stats.Series, orderedIds, nameById,
                valueSelector: (point, playerId) =>
                    point.AverageByPlayer.TryGetValue(playerId, out var v) ? v : (decimal?)null);
            _chartStore.CareerAverage.NightSlices = BuildSlicesFromSeries(stats.Series, orderedIds, nameById,
                valueSelector: (point, playerId) =>
                    cumulativeByChrono.TryGetValue(point.ChronologicalIndex, out var byPlayer)
                        && byPlayer.TryGetValue(playerId, out var v)
                        ? v
                        : (decimal?)null);

            // Nya PlotModels → kasta gamla annotation-referenser så
            // ApplySelection skapar nya på rätt slots.
            _chartStore.NightAverage.MarkerAnnotation = null;
            _chartStore.CareerAverage.MarkerAnnotation = null;

            // Applicera tidigare avvalda spelare på BÅDA grafer (kan ha
            // togglats i fullscreen innan användaren navigerade tillbaka,
            // eller från en tidigare session sett över datarefreshes).
            _chartStore.ApplyVisibilityToPlots();

            RebuildLegendItems(LegendItems, PlotModel, orderedIds, nameById);
            RebuildLegendItems(CareerLegendItems, CareerPlotModel, orderedIds, nameById);

            // Tracker-prenumeration på BÅDA modellerna så att tap på en
            // datapunkt i endera grafen uppdaterar SelectedNightIndex →
            // legend-snitt + markör i båda.
#pragma warning disable CS0618 // OxyPlot 2.2 TrackerChanged obsolete; ingen ersättning förrän 4.0 — se CLAUDE.md OxyPlot-gotcha
            PlotModel.TrackerChanged += OnPlotTrackerChanged;
            CareerPlotModel.TrackerChanged += OnPlotTrackerChanged;
#pragma warning restore CS0618

            // Behåll användarens val över LoadAsync-rebuilds; default = senaste
            // kvällen vid första laddning eller om sparade indexet är out-of-range.
            var storeIdx = _chartStore.SelectedNightIndex;
            var nightCount = _chartStore.NightAverage.NightSlices.Count;
            var defaultIdx = (storeIdx >= 0 && storeIdx < nightCount)
                ? storeIdx
                : (nightCount > 0 ? nightCount - 1 : -1);

            // Sätt index → triggar OnSelectedNightIndexChanged → ApplySelection.
            // Om idx samma som föregående LoadAsync fired ingen partial — kör
            // ApplySelection explicit som belt-and-braces.
            SelectedNightIndex = defaultIdx;
            ApplySelection();
            OnPropertyChanged(nameof(SelectedNightSlice));
            OnPropertyChanged(nameof(SelectedNightLabel));
            OnPropertyChanged(nameof(HasSelectedNight));

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

    // Beräknar löpande karriärsnitt per spelare per kväll. Returnerar map:
    // ChronologicalIndex → (PlayerId → kumulativt medel av kvällssnitten
    // för kväll 1..N). Med 4-spelar-invarianten har alla spelare värde varje
    // kväll, men vi gör defensiv null-check ändå.
    private static Dictionary<int, Dictionary<int, decimal>> BuildCumulativeCareerAverages(
        IReadOnlyList<NightAveragePoint> series,
        IReadOnlyList<int> orderedIds)
    {
        var sumByPlayer = orderedIds.ToDictionary(id => id, _ => 0m);
        var countByPlayer = orderedIds.ToDictionary(id => id, _ => 0);
        var result = new Dictionary<int, Dictionary<int, decimal>>(series.Count);

        foreach (var point in series)
        {
            var byPlayer = new Dictionary<int, decimal>(orderedIds.Count);
            foreach (var id in orderedIds)
            {
                if (!point.AverageByPlayer.TryGetValue(id, out var avg)) continue;
                sumByPlayer[id] += avg;
                countByPlayer[id]++;
                byPlayer[id] = sumByPlayer[id] / countByPlayer[id];
            }
            result[point.ChronologicalIndex] = byPlayer;
        }
        return result;
    }

    private static PlotModel BuildPlotModel(
        string title,
        IReadOnlyList<NightAveragePoint> series,
        Func<NightAveragePoint, int, decimal?> valueSelector,
        IReadOnlyList<int> orderedIds,
        IReadOnlyDictionary<int, string> nameById)
    {
        var model = new PlotModel
        {
            Title = title,
            TitleFontSize = 14,
            TitleColor = ChartForeground,
            Padding = new OxyThickness(8, 32, 16, 22),
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
                var value = valueSelector(point, id);
                if (value is null) continue;
                pointsByPlayer[id].Add(new NightSeriesPoint(
                    point.ChronologicalIndex,
                    (double)value.Value,
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
                MarkerType = MarkerType.None,
                StrokeThickness = 2,
                Color = color,
            };
            model.Series.Add(line);
        }

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

    private static void RebuildLegendItems(
        ObservableCollection<PlayerLegendItem> target,
        PlotModel model,
        IReadOnlyList<int> orderedIds,
        IReadOnlyDictionary<int, string> nameById)
    {
        target.Clear();
        for (int i = 0; i < orderedIds.Count; i++)
        {
            var name = nameById[orderedIds[i]];
            var series = model.Series.FirstOrDefault(s => string.Equals(s.Title, name, StringComparison.Ordinal));
            if (series is null) continue;

            var oxy = ColorForPlayer(name, i);
            var mauiColor = Microsoft.Maui.Graphics.Color.FromRgba(oxy.R, oxy.G, oxy.B, oxy.A);
            target.Add(new PlayerLegendItem(name, mauiColor, series.IsVisible));
        }
    }

    // Toggle på en spelarrad — synkar både legenders item.IsVisible och
    // båda PlotModels' Series.IsVisible. ApplyVisibilityToPlots iterar
    // store:ns slots; vi InvalidatePlot:ar båda så Skia renderar om.
    [RelayCommand]
    private void TogglePlayerVisibility(PlayerLegendItem? item)
    {
        if (item is null) return;

        var nowVisible = _chartStore.TogglePlayerVisibility(item.Name);

        SyncLegendEntry(LegendItems, item.Name, nowVisible);
        SyncLegendEntry(CareerLegendItems, item.Name, nowVisible);

        _chartStore.ApplyVisibilityToPlots();
        _chartStore.NightAverage.PlotModel?.InvalidatePlot(true);
        _chartStore.CareerAverage.PlotModel?.InvalidatePlot(true);
    }

    private static void SyncLegendEntry(
        ObservableCollection<PlayerLegendItem> legend,
        string playerName,
        bool nowVisible)
    {
        var entry = legend.FirstOrDefault(l => string.Equals(l.Name, playerName, StringComparison.Ordinal));
        if (entry is not null)
        {
            entry.IsVisible = nowVisible;
        }
    }

    // Slices för legenden: en per kväll med (DateLabel, per-spelar-värde).
    // Värdet bestäms av valueSelector — kvällssnitt eller löpande karriärsnitt.
    private static List<NightScrubberSlice> BuildSlicesFromSeries(
        IReadOnlyList<NightAveragePoint> series,
        IReadOnlyList<int> orderedIds,
        IReadOnlyDictionary<int, string> nameById,
        Func<NightAveragePoint, int, decimal?> valueSelector)
    {
        var slices = new List<NightScrubberSlice>(series.Count);
        foreach (var point in series)
        {
            var byName = new Dictionary<string, decimal>(4, StringComparer.OrdinalIgnoreCase);
            foreach (var id in orderedIds)
            {
                var value = valueSelector(point, id);
                if (value.HasValue)
                {
                    byName[nameById[id]] = value.Value;
                }
            }
            slices.Add(new NightScrubberSlice(
                point.ChronologicalIndex,
                BuildNightLabel(point),
                byName));
        }
        return slices;
    }

    private void OnPlotTrackerChanged(object? sender, OxyPlot.TrackerEventArgs e)
    {
        if (e.HitResult is null) return;
        var x = e.HitResult.DataPoint.X;
        var idx = (int)Math.Round(x) - 1;
        var nightCount = _chartStore.NightAverage.NightSlices.Count;
        if (idx < 0 || idx >= nightCount) return;
        if (idx == SelectedNightIndex) return;
        MainThread.BeginInvokeOnMainThread(() => SelectedNightIndex = idx);
    }

    private void ApplySelection()
    {
        // Synka legend-värdena per slot — kvällssnitt vs löpande karriärsnitt
        // sitter i respektive slots NightSlices, indexerat med samma
        // SelectedNightIndex (gemensam kronologi).
        UpdateLegendValues(_chartStore.NightAverage, LegendItems);
        UpdateLegendValues(_chartStore.CareerAverage, CareerLegendItems);

        // Markörlinjen i båda PlotModels rör sig till samma kväll.
        UpdateMarkerAnnotation(_chartStore.NightAverage);
        UpdateMarkerAnnotation(_chartStore.CareerAverage);

        // Spara valet i store så fullscreen tar över korrekt vald kväll
        // (och tvärtom när användaren går tillbaka).
        _chartStore.SelectedNightIndex = SelectedNightIndex;
    }

    private void UpdateLegendValues(GraphSlot slot, ObservableCollection<PlayerLegendItem> legend)
    {
        var slice = SelectedNightIndex >= 0 && SelectedNightIndex < slot.NightSlices.Count
            ? slot.NightSlices[SelectedNightIndex]
            : null;
        foreach (var item in legend)
        {
            item.NightAverage = slice is not null
                && slice.AverageByPlayerName.TryGetValue(item.Name, out var avg)
                ? avg
                : (decimal?)null;
        }
    }

    private void UpdateMarkerAnnotation(GraphSlot slot)
    {
        // Try/catch + Debug.WriteLine så ev. crash i annotation-pipelinen
        // surfar i VS Output istället för att ta ner appen — användaren
        // har inte adb, så Output-fönstret är enda fönstret in i runtime-fel.
        try
        {
            if (slot.PlotModel is null) return;

            var slice = SelectedNightIndex >= 0 && SelectedNightIndex < slot.NightSlices.Count
                ? slot.NightSlices[SelectedNightIndex]
                : null;

            // Om PlotModel byggts om sedan annotation:en skapades — kasta
            // gamla referensen så vi inte försöker peka i en stale modell.
            var ann = slot.MarkerAnnotation;
            if (ann is not null && !slot.PlotModel.Annotations.Contains(ann))
            {
                ann = null;
                slot.MarkerAnnotation = null;
            }

            if (slice is null)
            {
                if (ann is not null)
                {
                    slot.PlotModel.Annotations.Remove(ann);
                    slot.MarkerAnnotation = null;
                    slot.PlotModel.InvalidatePlot(false);
                }
                return;
            }

            if (ann is null)
            {
                ann = new LineAnnotation
                {
                    Type = LineAnnotationType.Vertical,
                    Color = MarkerColor,
                    StrokeThickness = 1,
                    LineStyle = LineStyle.Solid,
                    ClipByYAxis = true,
                };
                slot.PlotModel.Annotations.Add(ann);
                slot.MarkerAnnotation = ann;
            }
            ann.X = slice.ChronologicalIndex;
            slot.PlotModel.InvalidatePlot(false);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[UpdateMarkerAnnotation] {ex.GetType().Name}: {ex.Message}");
            Debug.WriteLine(ex.StackTrace);
        }
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
