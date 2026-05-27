using OxyPlot;

namespace DoubleDashScore.Services;

/// Singleton som delar den senast byggda PlotModel mellan HistoryStatsViewModel
/// (som bygger den) och FullScreenChartViewModel (som visar den på helskärm).
/// Anledningen till stuget: PlotModel går inte att skicka via Shell-navigations-
/// query-string, och att bygga om den i fullscreen-vyn skulle innebära dubbel
/// repo-laddning + duplicerad BuildPlotModel-kod.
///
/// Lagrar också vilka spelarlinjer som är avvalda i grafen, så att toggling
/// i ena vyn (vanlig statistik eller fullscreen) syns även i den andra.
/// HistoryStatsViewModel.LoadAsync bygger om PlotModel:en — efter bygget
/// appliceras HiddenPlayerNames på serie-synligheten så att state överlever
/// rebuilds.
public sealed class ChartTransferStore
{
    public PlotModel? CurrentPlotModel { get; set; }

    public HashSet<string> HiddenPlayerNames { get; } =
        new(StringComparer.OrdinalIgnoreCase);

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
