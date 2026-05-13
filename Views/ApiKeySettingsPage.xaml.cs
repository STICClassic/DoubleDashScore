using DoubleDashScore.ViewModels;

namespace DoubleDashScore.Views;

public partial class ApiKeySettingsPage : ContentPage
{
    private readonly ApiKeySettingsViewModel _vm;

    public ApiKeySettingsPage(ApiKeySettingsViewModel vm)
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
