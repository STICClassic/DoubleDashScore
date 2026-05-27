using OxyPlot;

namespace DoubleDashScore.Services;

/// Singleton som delar den senast byggda PlotModel mellan HistoryStatsViewModel
/// (som bygger den) och FullScreenChartViewModel (som visar den på helskärm).
/// Anledningen till stuget: PlotModel går inte att skicka via Shell-navigations-
/// query-string, och att bygga om den i fullscreen-vyn skulle innebära dubbel
/// repo-laddning + duplicerad BuildPlotModel-kod.
///
/// Lagrar också:
/// - Vilka spelarlinjer som är avvalda i grafen (toggle-state, Skiva 12).
/// - Slice-listan + vald kväll-index (Skiva 15) så att tap-i-grafen-→-uppdatera-
///   legend-snitt synkar mellan vanlig graf och fullscreen.
public sealed class ChartTransferStore
{
    public PlotModel? CurrentPlotModel { get; set; }

    public HashSet<string> HiddenPlayerNames { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    // Alla kvällar i kronologisk ordning, projicerade från stats.Series.
    // Återbyggs varje LoadAsync i HistoryStatsViewModel. FullScreenChartViewModel
    // läser denna när dess sida visas så att tap-på-punkt-→-legend-snitt
    // fungerar utan att fullscreen behöver dubbel-läsa repo:t.
    public IReadOnlyList<NightScrubberSlice> NightSlices { get; set; } =
        Array.Empty<NightScrubberSlice>();

    // Vald kväll-index i NightSlices. -1 om inget val. Bevaras över navigering
    // mellan vanlig graf och fullscreen så användaren inte tappar sin valda
    // kväll vid fullscreen-toggle.
    public int SelectedNightIndex { get; set; } = -1;

    public bool IsVisible(string playerName) =>
        !HiddenPlayerNames.Contains(playerName);

    // Returnerar den nya synligheten (true = visible) efter toggle.
    public bool TogglePlayerVisibility(string playerName)
    {
        if (HiddenPlayerNames.Contains(playerName))
        {
            HiddenPlayerNames.Remove(playerName);
            return true;
        }
        HiddenPlayerNames.Add(playerName);
        return false;
    }

    // Synkar synligheten i PlotModel.Series mot HiddenPlayerNames. Returnerar
    // antal series som ändrades så caller kan undvika onödig InvalidatePlot.
    public int ApplyVisibilityToPlot()
    {
        if (CurrentPlotModel is null) return 0;
        int changed = 0;
        foreach (var series in CurrentPlotModel.Series)
        {
            var shouldBeVisible = IsVisible(series.Title);
            if (series.IsVisible != shouldBeVisible)
            {
                series.IsVisible = shouldBeVisible;
                changed++;
            }
        }
        return changed;
    }
}
