using OxyPlot;

namespace DoubleDashScore.Services;

/// Singleton som delar den senast byggda PlotModel mellan HistoryStatsViewModel
/// (som bygger den) och FullScreenChartViewModel (som visar den på helskärm).
/// Anledningen till stuget: PlotModel går inte att skicka via Shell-navigations-
/// query-string, och att bygga om den i fullscreen-vyn skulle innebära dubbel
/// repo-laddning + duplicerad BuildPlotModel-kod.
public sealed class ChartTransferStore
{
    public PlotModel? CurrentPlotModel { get; set; }
}
