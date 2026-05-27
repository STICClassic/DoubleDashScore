using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DoubleDashScore.Services;
using Microsoft.Maui.Graphics;
using OxyPlot;

namespace DoubleDashScore.ViewModels;

public sealed partial class FullScreenChartViewModel : ObservableObject
{
    private static readonly TimeSpan AutoHideDelay = TimeSpan.FromSeconds(3);

    private readonly ChartTransferStore _store;
    private CancellationTokenSource? _hideCts;

    public FullScreenChartViewModel(ChartTransferStore store)
    {
        _store = store;
    }

    public PlotModel? PlotModel => _store.CurrentPlotModel;

    // Egen legend, byggs om varje gång sidan visas mot _store.CurrentPlotModel.
    // Toggling delar HiddenPlayerNames-set med HistoryStatsViewModel via store.
    public ObservableCollection<PlayerLegendItem> LegendItems { get; } = new();

    // True när ✕- och ↺-knapparna ska vara synliga. Sidan kopplar denna till
    // FadeTo + InputTransparent på knapparna.
    [ObservableProperty]
    private bool _isControlsVisible = true;

    public void OnPageAppearing()
    {
        IsControlsVisible = true;
        StartHideTimer();
        RebuildLegendItems();
    }

    public void OnPageDisappearing()
    {
        CancelHideTimer();
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
