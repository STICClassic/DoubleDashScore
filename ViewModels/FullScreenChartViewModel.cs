using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DoubleDashScore.Services;
using OxyPlot;

namespace DoubleDashScore.ViewModels;

public sealed partial class FullScreenChartViewModel : ObservableObject
{
    private readonly ChartTransferStore _store;

    public FullScreenChartViewModel(ChartTransferStore store)
    {
        _store = store;
    }

    public PlotModel? PlotModel => _store.CurrentPlotModel;

    [RelayCommand]
    private static async Task Close()
    {
        await Shell.Current.GoToAsync("..").ConfigureAwait(true);
    }
}
