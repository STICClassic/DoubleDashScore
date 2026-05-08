using DoubleDashScore.ViewModels;

namespace DoubleDashScore.Views;

public partial class NewNightPage : ContentPage
{
    public NewNightPage(NewNightViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
