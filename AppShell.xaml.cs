using DoubleDashScore.ViewModels;
using DoubleDashScore.Views;

namespace DoubleDashScore;

public partial class AppShell : Shell
{
    private readonly AppShellViewModel _vm;

    public AppShell(AppShellViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = vm;

        // Sätt FlyoutWidth till ~1/3 av enhetsbredden (i device-independent pixels).
        // Golv på 240 dp så textetiketter ryms på smala telefoner (~360 dp där 33%
        // skulle ge 120 dp och vara oläsbart).
        var display = DeviceDisplay.Current.MainDisplayInfo;
        var widthDp = display.Width / display.Density;
        FlyoutWidth = Math.Max(widthDp / 3.0, 240);

        // Rotsidor (NightsListPage, HistoryStatsPage, PlayerEditPage, ApiKeySettingsPage)
        // är ShellContent-items i XAML — de auto-registreras som rutter och nås via
        // absolut navigation `//Route`. Här registreras bara push-stack-sidor (modaler/
        // detaljvyer som öppnas via knappar inifrån en sida).
        Routing.RegisterRoute("NewNightPage", typeof(NewNightPage));
        Routing.RegisterRoute("NightDetailPage", typeof(NightDetailPage));
        Routing.RegisterRoute("RoundEntryPage", typeof(RoundEntryPage));
        Routing.RegisterRoute("NightStatsPage", typeof(NightStatsPage));
        Routing.RegisterRoute("FullScreenChartPage", typeof(FullScreenChartPage));
        Routing.RegisterRoute("OcrPreviewPage", typeof(OcrPreviewPage));

        Navigated += OnShellNavigated;
        UpdateSelectedRoute();
    }

    private void OnShellNavigated(object? sender, ShellNavigatedEventArgs e)
    {
        UpdateSelectedRoute();
    }

    private void UpdateSelectedRoute()
    {
        // CurrentState.Location ser ut som "//NightsListPage" eller
        // "//NightsListPage/NightDetailPage" när detaljvy är pushed. Plocka det
        // första segmentet efter // som rot-route. Tom om appen inte hunnit
        // navigera än.
        var path = CurrentState?.Location?.OriginalString ?? string.Empty;
        var trimmed = path.TrimStart('/');
        var firstSegment = trimmed.Split('/', 2, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault() ?? string.Empty;
        _vm.SelectedRoute = firstSegment;
    }
}
