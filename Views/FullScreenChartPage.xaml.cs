using DoubleDashScore.ViewModels;

namespace DoubleDashScore.Views;

public partial class FullScreenChartPage : ContentPage
{
    public FullScreenChartPage(FullScreenChartViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
#if ANDROID
        if (Microsoft.Maui.ApplicationModel.Platform.CurrentActivity is MainActivity activity)
        {
            activity.EnterFullscreen();
        }
#endif
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
#if ANDROID
        if (Microsoft.Maui.ApplicationModel.Platform.CurrentActivity is MainActivity activity)
        {
            activity.ExitFullscreen();
        }
#endif
    }
}
