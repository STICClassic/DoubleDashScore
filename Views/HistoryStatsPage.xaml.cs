using DoubleDashScore.ViewModels;

namespace DoubleDashScore.Views;

public partial class HistoryStatsPage : ContentPage
{
    private readonly HistoryStatsViewModel _vm;

    public HistoryStatsPage(HistoryStatsViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.LoadAsync();
    }
}
