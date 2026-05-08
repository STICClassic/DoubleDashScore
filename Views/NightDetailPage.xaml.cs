using DoubleDashScore.ViewModels;

namespace DoubleDashScore.Views;

public partial class NightDetailPage : ContentPage
{
    private readonly NightDetailViewModel _vm;

    public NightDetailPage(NightDetailViewModel vm)
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
