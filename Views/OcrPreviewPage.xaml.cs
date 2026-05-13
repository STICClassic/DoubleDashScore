using DoubleDashScore.ViewModels;

namespace DoubleDashScore.Views;

public partial class OcrPreviewPage : ContentPage
{
    private readonly OcrPreviewViewModel _vm;

    public OcrPreviewPage(OcrPreviewViewModel vm)
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
