using DoubleDashScore.Data;
using DoubleDashScore.ViewModels;

namespace DoubleDashScore.Views;

public partial class ImportPreviewPage : ContentPage
{
    private readonly TaskCompletionSource<ImportChoice?> _tcs = new();
    private readonly ImportPreviewViewModel _vm;

    public Task<ImportChoice?> Result => _tcs.Task;

    public ImportPreviewPage(ImportPreview preview)
    {
        InitializeComponent();
        _vm = new ImportPreviewViewModel(preview);
        _vm.CompletionRequested += OnCompletion;
        BindingContext = _vm;
    }

    private async void OnCompletion(ImportChoice? choice)
    {
        _tcs.TrySetResult(choice);
        await Navigation.PopModalAsync();
    }

    protected override bool OnBackButtonPressed()
    {
        // Hårdvaru-back räknas som Avbryt.
        _tcs.TrySetResult(null);
        return base.OnBackButtonPressed();
    }
}
