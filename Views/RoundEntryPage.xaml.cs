using DoubleDashScore.ViewModels;

namespace DoubleDashScore.Views;

public partial class RoundEntryPage : ContentPage
{
    private readonly RoundEntryViewModel _vm;

    public RoundEntryPage(RoundEntryViewModel vm)
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
