using DoubleDashScore.ViewModels;

namespace DoubleDashScore.Views;

public partial class NightsListPage : ContentPage
{
    private readonly NightsListViewModel _vm;

    public NightsListPage(NightsListViewModel vm)
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
