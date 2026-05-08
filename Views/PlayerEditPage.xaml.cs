using DoubleDashScore.ViewModels;

namespace DoubleDashScore.Views;

public partial class PlayerEditPage : ContentPage
{
    private readonly PlayerEditViewModel _vm;

    public PlayerEditPage(PlayerEditViewModel vm)
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
