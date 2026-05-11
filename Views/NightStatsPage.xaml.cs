using DoubleDashScore.ViewModels;

namespace DoubleDashScore.Views;

public partial class NightStatsPage : ContentPage
{
    private readonly NightStatsViewModel _vm;

    public NightStatsPage(NightStatsViewModel vm)
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
