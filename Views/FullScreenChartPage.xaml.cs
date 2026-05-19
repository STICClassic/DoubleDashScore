using System.ComponentModel;
using DoubleDashScore.ViewModels;

namespace DoubleDashScore.Views;

public partial class FullScreenChartPage : ContentPage
{
    private const uint FadeDurationMs = 200;

    private readonly FullScreenChartViewModel _vm;

    public FullScreenChartPage(FullScreenChartViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
        _vm = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _vm.PropertyChanged += OnVmPropertyChanged;
        ApplyControlsVisibility(_vm.IsControlsVisible, animate: false);
        _vm.OnPageAppearing();
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
        _vm.PropertyChanged -= OnVmPropertyChanged;
        _vm.OnPageDisappearing();
#if ANDROID
        if (Microsoft.Maui.ApplicationModel.Platform.CurrentActivity is MainActivity activity)
        {
            activity.ExitFullscreen();
        }
#endif
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FullScreenChartViewModel.IsControlsVisible))
        {
            ApplyControlsVisibility(_vm.IsControlsVisible, animate: true);
        }
    }

    private void ApplyControlsVisibility(bool visible, bool animate)
    {
        // InputTransparent sätts synkront så att en dold knapp aldrig konsumerar
        // tap-eventet — tappen ska gå rakt till PlotView:s tap-gesture som
        // togglar tillbaka kontrollerna synliga.
        ResetButton.InputTransparent = !visible;
        CloseButton.InputTransparent = !visible;

        var target = visible ? 1.0 : 0.0;
        if (!animate)
        {
            ResetButton.Opacity = target;
            CloseButton.Opacity = target;
            return;
        }
        _ = ResetButton.FadeToAsync(target, FadeDurationMs);
        _ = CloseButton.FadeToAsync(target, FadeDurationMs);
    }
}
