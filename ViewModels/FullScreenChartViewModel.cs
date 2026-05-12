using CommunityToolkit.Mvvm.ComponentModel;
using DoubleDashScore.Services;
using OxyPlot;

namespace DoubleDashScore.ViewModels;

public sealed class FullScreenChartViewModel : ObservableObject
{
    private readonly ChartTransferStore _store;

    public FullScreenChartViewModel(ChartTransferStore store)
    {
        _store = store;
    }

    public PlotModel? PlotModel => _store.CurrentPlotModel;
}
