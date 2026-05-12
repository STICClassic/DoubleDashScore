using DoubleDashScore.ViewModels;

namespace DoubleDashScore.Views;

public partial class FullScreenChartPage : ContentPage
{
    public FullScreenChartPage(FullScreenChartViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
