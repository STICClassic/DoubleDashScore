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
    private LineAnnotation? _markerAnnotation;
    private PlotModel? _subscribedModel;

    public FullScreenChartViewModel(ChartTransferStore store)
    {
        _store = store;
    }

    public PlotModel? PlotModel => _store.CurrentPlotModel;

    // Egen legend, byggs om varje gång sidan visas mot _store.CurrentPlotModel.
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
        SelectedNightIndex >= 0 && SelectedNightIndex < _store.NightSlices.Count
            ? _store.NightSlices[SelectedNightIndex]
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
        var model = _store.CurrentPlotModel;
        if (_markerAnnotation is null || model is null) return;
        _markerAnnotation.Color = IsControlsVisible ? MarkerColorActive : OxyColors.Transparent;
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
        var defaultIdx = (storeIdx >= 0 && storeIdx < _store.NightSlices.Count)
            ? storeIdx
            : (_store.NightSlices.Count > 0 ? _store.NightSlices.Count - 1 : -1);
        _markerAnnotation = null; // refresh mot nuvarande PlotModel
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
    }

    // Tap på själva grafen togglar — knapparna har egna commands och
    // restart:ar i stället för att toggle:a.
    [RelayCommand]
    private void TogglePlotTap()
    {
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
        var model = _store.CurrentPlotModel;
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
        var model = _store.CurrentPlotModel;
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
        var model = _store.CurrentPlotModel;
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
        var model = _store.CurrentPlotModel;
        if (model is null) return;
        if (ReferenceEquals(_subscribedModel, model)) return;

        // Avregistrera från eventuell tidigare modell innan vi byter.
        UnsubscribeTrackerChanged();
        model.TrackerChanged += OnPlotTrackerChanged;
        _subscribedModel = model;
    }

    private void UnsubscribeTrackerChanged()
    {
        if (_subscribedModel is not null)
        {
            _subscribedModel.TrackerChanged -= OnPlotTrackerChanged;
            _subscribedModel = null;
        }
    }

    private void OnPlotTrackerChanged(object? sender, TrackerEventArgs e)
    {
        if (e.HitResult is null) return;
        var x = e.HitResult.DataPoint.X;
        var idx = (int)Math.Round(x) - 1;
        if (idx < 0 || idx >= _store.NightSlices.Count) return;
        if (idx == SelectedNightIndex) return;
        MainThread.BeginInvokeOnMainThread(() => SelectedNightIndex = idx);
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
            var model = _store.CurrentPlotModel;
            if (model is null) return;

            if (_markerAnnotation is not null && !model.Annotations.Contains(_markerAnnotation))
            {
                _markerAnnotation = null;
            }

            if (slice is null)
            {
                if (_markerAnnotation is not null)
                {
                    model.Annotations.Remove(_markerAnnotation);
                    _markerAnnotation = null;
                    model.InvalidatePlot(false);
                }
                return;
            }

            if (_markerAnnotation is null)
            {
                _markerAnnotation = new LineAnnotation
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
                model.Annotations.Add(_markerAnnotation);
            }
            _markerAnnotation.X = slice.ChronologicalIndex;
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
