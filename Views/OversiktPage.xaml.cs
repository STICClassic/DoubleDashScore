using DoubleDashScore.ViewModels;

namespace DoubleDashScore.Views;

public partial class OversiktPage : ContentPage
{
    private readonly OversiktViewModel _vm;

    public OversiktPage(OversiktViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
        _vm = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.LoadAsync().ConfigureAwait(true);
    }
}
