using DoubleDashScore.ViewModels;

namespace DoubleDashScore.Views;

public partial class RestoreAutoBackupPage : ContentPage
{
    private readonly RestoreAutoBackupViewModel _vm;

    public RestoreAutoBackupPage(RestoreAutoBackupViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _vm.Load();
    }
}
