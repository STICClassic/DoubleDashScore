using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DoubleDashScore.Services;
using Microsoft.Maui.Graphics;
using OxyPlot;
using OxyPlot.Annotations;

namespace DoubleDashScore.ViewModels;

public sealed partial class FullScreenChartViewModel : ObservableObject
{
    private static readonly TimeSpan AutoHideDelay = TimeSpan.FromSeconds(3);

    // Markörlinjens "synlig"-färg. Diskret tunn grå med ~55 % alfa — syns
    // mot grå plot-bakgrund (#C8C8C8) men dominerar inte över spelarlinjerna.
    // Extraherad till konstant så toggle mellan synlig och dold (transparent)
    // inte duplicerar magiska värden.
    private static readonly OxyColor MarkerColorActive = OxyColor.FromAColor(140, OxyColors.Gray);

    private readonly ChartTransferStore _store;
    private CancellationTokenSource? _hideCts;
    private PlotModel? _subscribedModel;

    // Sätts synkront av OnPlotTrackerChanged när en datapunkt-tap har valt
    // en NY kväll. TogglePlotTap defer:as via dispatchern och kollar
    // flaggan — om satt skippar den toggle:n (tracker har redan satt
    // IsControlsVisible=true + startat timer). Konflikten beror på att
    // TapGestureRecognizer + OxyPlot:s tracker triggas båda av samma
    // touch utan garanterad ordning; flaggan + defer ger deterministisk
    // hantering oavsett vem som fyrar först.
    private bool _trackerHandledRecentTap;

    public FullScreenChartViewModel(ChartTransferStore store)
    {
        _store = store;
    }

    public PlotModel? PlotModel => _store.Active.PlotModel;

    // Egen legend, byggs om varje gång sidan visas mot _store.Active.PlotModel.
    // Toggling delar HiddenPlayerNames-set med HistoryStatsViewModel via store.
    // Varje item:s NightAverage uppdateras när användaren tap:ar en punkt
    // i grafen — synkar med _store.SelectedNightIndex så vald kväll bevaras
    // mellan vanlig graf och fullscreen.
    public ObservableCollection<PlayerLegendItem> LegendItems { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedNightSlice))]
    [NotifyPropertyChangedFor(nameof(SelectedNightLabel))]
    [NotifyPropertyChangedFor(nameof(HasSelectedNight))]
    private int _selectedNightIndex = -1;

    public NightScrubberSlice? SelectedNightSlice =>
        SelectedNightIndex >= 0 && SelectedNightIndex < _store.Active.NightSlices.Count
            ? _store.Active.NightSlices[SelectedNightIndex]
            : null;

    public string SelectedNightLabel => SelectedNightSlice?.DateLabel ?? string.Empty;

    public bool HasSelectedNight => SelectedNightSlice is not null;

    partial void OnSelectedNightIndexChanged(int value)
    {
        ApplySelection();
    }

    // True när ✕- och ↺-knapparna ska vara synliga. Sidan kopplar denna till
    // FadeTo + InputTransparent på knapparna. Markörlinjen (vertikal
    // LineAnnotation) följer också detta state via OnIsControlsVisibleChanged
    // → UpdateMarkerColor, så den auto-döljs tillsammans med knapparna.
    [ObservableProperty]
    private bool _isControlsVisible = true;

    // Markörlinjen är en OxyPlot-annotation och kan inte fadas via MAUI
    // Opacity. Vi togglar Color mellan alfagrå och transparent + invaliderar
    // plotten — visuellt blir det en hård av/på som sker i takt med
    // ✕/↺ FadeTo-animationen. Renare än add/remove från Annotations-listan
    // (som skulle tappa referensen och tvinga oss att återskapa annotationen
    // för varje toggle).
    partial void OnIsControlsVisibleChanged(bool value)
    {
        UpdateMarkerColor();
    }

    private void UpdateMarkerColor()
    {
        var ann = _store.Active.MarkerAnnotation;
        var model = _store.Active.PlotModel;
        if (ann is null || model is null) return;
        ann.Color = IsControlsVisible ? MarkerColorActive : OxyColors.Transparent;
        model.InvalidatePlot(false);
    }

    // Pulldown-overlay från övre kanten: dold som default (bara pil-fliken
    // syns). Innehållet (Kväll N: + spelarkolumnerna) togglas via IsVisible.
    // Border:n krymper då med innehållet — fliken sitter alltid i botten av
    // Border:n så den glider med när panelen växer/krymper.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LegendCollapseArrow))]
    [NotifyPropertyChangedFor(nameof(IsLegendCollapsed))]
    private bool _isLegendExpanded = false;

    // ▼ när dold (tap → glid in uppifrån), ▲ när synlig (tap → glid upp/dölj).
    public string LegendCollapseArrow => IsLegendExpanded ? "▲" : "▼";

    // Invers av IsLegendExpanded — bunden till färgförklaringens IsVisible
    // i XAML. När legenden är utfälld täcker den innehållet (spelarrutorna
    // visar namn + färg), så färgförklaringen behövs inte och döljs.
    public bool IsLegendCollapsed => !IsLegendExpanded;

    [RelayCommand]
    private void ToggleLegendCollapse()
    {
        IsLegendExpanded = !IsLegendExpanded;
        // Räkna tap på fliken som interaktion så ✕/↺-knapparna hålls synliga.
        IsControlsVisible = true;
        StartHideTimer();
    }

    public void OnPageAppearing()
    {
        IsControlsVisible = true;
        StartHideTimer();
        RebuildLegendItems();
        SubscribeTrackerChanged();

        // Initiera vald kväll från store. HistoryStatsViewModel sätter den
        // när dess LoadAsync körs; default = senaste kvällen.
        var storeIdx = _store.SelectedNightIndex;
        var defaultIdx = (storeIdx >= 0 && storeIdx < _store.Active.NightSlices.Count)
            ? storeIdx
            : (_store.Active.NightSlices.Count > 0 ? _store.Active.NightSlices.Count - 1 : -1);
        // Markörlinjen är delad via store (skapad av portrait första gången
        // PlotModel byggs). UpdateMarkerAnnotation reuse:ar den om den är
        // i nuvarande modell — vi behöver inte nulla något här.
        SelectedNightIndex = defaultIdx;
        ApplySelection();
        OnPropertyChanged(nameof(SelectedNightSlice));
        OnPropertyChanged(nameof(SelectedNightLabel));
        OnPropertyChanged(nameof(HasSelectedNight));
    }

    public void OnPageDisappearing()
    {
        CancelHideTimer();
        UnsubscribeTrackerChanged();

        // Auto-hide hör bara till fullscreen-vyn. Återställ markörens färg
        // till MarkerColorActive så portrait-vyn (som inte har auto-hide)
        // ser den permanent synlig när användaren navigerar tillbaka.
        // Utan denna restore skulle markören förbli transparent i portrait
        // om fullscreen stängdes medan controls var auto-dolda.
        var ann = _store.Active.MarkerAnnotation;
        var model = _store.Active.PlotModel;
        if (ann is not null && model is not null)
        {
            ann.Color = MarkerColorActive;
            model.InvalidatePlot(false);
        }
    }

    // Tap på själva grafen togglar — knapparna har egna commands och
    // restart:ar i stället för att toggle:a. OBS: TogglePlotTap fyrar
    // för ALLA taps inkl. taps som träffar en datapunkt (där OxyPlot:s
    // TrackerChanged också fyrar). Vi defer:ar via BeginInvokeOnMainThread
    // så _trackerHandledRecentTap-flaggan hinner sättas synkront av
    // OnPlotTrackerChanged om den fyrade — då skippar vi toggle:n och
    // låter tracker-handlern äga visningen (den sätter IsControlsVisible
    // =true + startar timer för att visa kontrollerna vid den nya valda
    // kvällen). Defer:n är robust mot ordningen mellan TapGestureRecognizer
    // och OxyPlot:s interna tracker — vem som än fyrar först, kollas
    // flaggan när dispatchern processar lambda:n.
    [RelayCommand]
    private void TogglePlotTap()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (_trackerHandledRecentTap)
            {
                // Tracker valde redan en ny kväll och visade kontrollerna —
                // toggla inte bort dem. Reset så nästa tap utvärderas rent.
                _trackerHandledRecentTap = false;
                return;
            }
            if (IsControlsVisible)
            {
                CancelHideTimer();
                IsControlsVisible = false;
            }
            else
            {
                IsControlsVisible = true;
                StartHideTimer();
            }
        });
    }

    [RelayCommand]
    private async Task Close()
    {
        CancelHideTimer();
        await Shell.Current.GoToAsync("..").ConfigureAwait(true);
    }

    [RelayCommand]
    private void ResetZoom()
    {
        var model = _store.Active.PlotModel;
        if (model is null) return;
        model.ResetAllAxes();
        // false → behåll data, rita bara om med återställda axlar.
        model.InvalidatePlot(false);
        // Tap på en synlig knapp ska räknas som interaktion: håll knapparna
        // synliga och starta om 3-sekunderstimern.
        StartHideTimer();
    }

    [RelayCommand]
    private void TogglePlayerVisibility(PlayerLegendItem? item)
    {
        if (item is null) return;
        var model = _store.Active.PlotModel;
        if (model is null) return;

        var nowVisible = _store.TogglePlayerVisibility(item.Name);
        item.IsVisible = nowVisible;

        var series = model.Series.FirstOrDefault(s => string.Equals(s.Title, item.Name, StringComparison.Ordinal));
        if (series is null) return;
        series.IsVisible = nowVisible;
        model.InvalidatePlot(true);

        // Tap på en legend-rad räknas som interaktion — håll kontrollerna
        // synliga lite till så användaren kan toggla flera spelare.
        IsControlsVisible = true;
        StartHideTimer();
    }

    private void RebuildLegendItems()
    {
        LegendItems.Clear();
        var model = _store.Active.PlotModel;
        if (model is null) return;

        foreach (var series in model.Series)
        {
            if (series is not OxyPlot.Series.LineSeries line) continue;
            var oxy = line.Color;
            var color = Color.FromRgba(oxy.R, oxy.G, oxy.B, oxy.A);
            LegendItems.Add(new PlayerLegendItem(line.Title, color, line.IsVisible));
        }
    }

    private void SubscribeTrackerChanged()
    {
        var model = _store.Active.PlotModel;
        if (model is null) return;
        if (ReferenceEquals(_subscribedModel, model)) return;

        // Avregistrera från eventuell tidigare modell innan vi byter.
        UnsubscribeTrackerChanged();
#pragma warning disable CS0618 // OxyPlot 2.2 TrackerChanged obsolete; ingen ersättning förrän 4.0 — se CLAUDE.md OxyPlot-gotcha
        model.TrackerChanged += OnPlotTrackerChanged;
#pragma warning restore CS0618
        _subscribedModel = model;
    }

    private void UnsubscribeTrackerChanged()
    {
        if (_subscribedModel is not null)
        {
#pragma warning disable CS0618 // OxyPlot 2.2 TrackerChanged obsolete; ingen ersättning förrän 4.0 — se CLAUDE.md OxyPlot-gotcha
            _subscribedModel.TrackerChanged -= OnPlotTrackerChanged;
#pragma warning restore CS0618
            _subscribedModel = null;
        }
    }

    private void OnPlotTrackerChanged(object? sender, TrackerEventArgs e)
    {
        if (e.HitResult is null) return;
        var x = e.HitResult.DataPoint.X;
        var idx = (int)Math.Round(x) - 1;
        if (idx < 0 || idx >= _store.Active.NightSlices.Count) return;
        // Samma kväll = behandla som tom-tap → låt TogglePlotTap toggla
        // kontrollerna som vanligt. Lämna flaggan oförändrad.
        if (idx == SelectedNightIndex) return;

        // Synkront: markera att tracker hanterar denna touch så TogglePlotTap
        // skippar sin toggle. Måste sättas FÖRE BeginInvokeOnMainThread så
        // den syns för tap-handlern oavsett vem som dispatchas först.
        _trackerHandledRecentTap = true;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            SelectedNightIndex = idx;
            // Ny kväll vald: visa kontroller + markörlinjen vid nya kvällen
            // och starta om 3 s-timern. ApplySelection (via partial method)
            // har redan flyttat markörens X; här synkar vi även visnings-
            // state så markören blir grå och knapparna fadar in.
            IsControlsVisible = true;
            StartHideTimer();
        });
    }

    private void ApplySelection()
    {
        var slice = SelectedNightSlice;

        foreach (var item in LegendItems)
        {
            item.NightAverage = slice is not null
                && slice.AverageByPlayerName.TryGetValue(item.Name, out var avg)
                ? avg
                : (decimal?)null;
        }

        UpdateMarkerAnnotation(slice);

        _store.SelectedNightIndex = SelectedNightIndex;
    }

    private void UpdateMarkerAnnotation(NightScrubberSlice? slice)
    {
        // Try/catch + Debug.WriteLine så ev. crash i annotation-pipelinen
        // surfar i VS Output istället för att ta ner appen — användaren
        // har inte adb, så Output-fönstret är enda fönstret in i runtime-fel.
        try
        {
            var model = _store.Active.PlotModel;
            if (model is null) return;

            // Markörlinjen delas via store. Om instansen finns men inte är
            // i nuvarande modells Annotations-lista (modellen byggdes om
            // sedan referensen sparades) → nulla och skapa ny nedan.
            var ann = _store.Active.MarkerAnnotation;
            if (ann is not null && !model.Annotations.Contains(ann))
            {
                ann = null;
                _store.Active.MarkerAnnotation = null;
            }

            if (slice is null)
            {
                if (ann is not null)
                {
                    model.Annotations.Remove(ann);
                    _store.Active.MarkerAnnotation = null;
                    model.InvalidatePlot(false);
                }
                return;
            }

            if (ann is null)
            {
                ann = new LineAnnotation
                {
                    Type = LineAnnotationType.Vertical,
                    // Initial färg följer auto-hide-state: synlig (MarkerColor-
                    // Active = grå alpha 140) vid start, transparent om controls
                    // redan hunnit fadas ut innan annotationen hann skapas
                    // (osannolikt i praktiken men billigt att hantera).
                    Color = IsControlsVisible ? MarkerColorActive : OxyColors.Transparent,
                    StrokeThickness = 1,
                    LineStyle = LineStyle.Solid,
                    ClipByYAxis = true,
                };
                model.Annotations.Add(ann);
                _store.Active.MarkerAnnotation = ann;
            }
            ann.X = slice.ChronologicalIndex;
            model.InvalidatePlot(false);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[UpdateMarkerAnnotation] {ex.GetType().Name}: {ex.Message}");
            Debug.WriteLine(ex.StackTrace);
        }
    }

    private void StartHideTimer()
    {
        CancelHideTimer();
        var cts = new CancellationTokenSource();
        _hideCts = cts;
        _ = HideAfterDelayAsync(cts.Token);
    }

    private async Task HideAfterDelayAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(AutoHideDelay, ct).ConfigureAwait(true);
        }
        catch (TaskCanceledException)
        {
            return;
        }
        if (!ct.IsCancellationRequested)
        {
            IsControlsVisible = false;
        }
    }

    private void CancelHideTimer()
    {
        var cts = _hideCts;
        _hideCts = null;
        cts?.Cancel();
        cts?.Dispose();
    }
}
