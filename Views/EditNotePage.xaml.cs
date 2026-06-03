using DoubleDashScore.ViewModels;

namespace DoubleDashScore.Views;

public partial class EditNotePage : ContentPage
{
    private readonly EditNoteViewModel _vm;

    public EditNotePage(EditNoteViewModel vm)
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
