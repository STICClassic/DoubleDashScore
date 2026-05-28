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
        // Både controls- och legend-state påverkar Border:ns synlighet:
        // när legenden är utfälld ska den alltid synas; när infälld följer
        // den ✕/↺-knapparnas auto-hide.
        if (e.PropertyName == nameof(FullScreenChartViewModel.IsControlsVisible)
            || e.PropertyName == nameof(FullScreenChartViewModel.IsLegendExpanded))
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

        // Pulldown-overlay:n följer auto-hide när legenden är infälld
        // (bara fliken syns, beter sig som ✕/↺). När legenden är utfälld
        // ska hela panelen ligga kvar och vara opåverkad av tap-toggle —
        // den auto-döljs alltså inte. legendVisible reflekterar detta.
        var legendVisible = _vm.IsLegendExpanded || visible;
        LegendBorder.InputTransparent = !legendVisible;

        var target = visible ? 1.0 : 0.0;
        var legendTarget = legendVisible ? 1.0 : 0.0;
        if (!animate)
        {
            ResetButton.Opacity = target;
            CloseButton.Opacity = target;
            LegendBorder.Opacity = legendTarget;
            return;
        }
        _ = ResetButton.FadeToAsync(target, FadeDurationMs);
        _ = CloseButton.FadeToAsync(target, FadeDurationMs);
        _ = LegendBorder.FadeToAsync(legendTarget, FadeDurationMs);
    }
}
