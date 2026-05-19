namespace DoubleDashScore.Views;

public partial class SplashPage : ContentPage
{
    private readonly AppShell _shell;
    private bool _animationStarted;

    public SplashPage(AppShell shell)
    {
        InitializeComponent();
        _shell = shell;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        // Skydda mot dubbla körningar vid t.ex. orientering eller resume —
        // animationen ska köra exakt en gång per app-start.
        if (_animationStarted) return;
        _animationStarted = true;

        try
        {
            await RunSplashAnimationAsync();
        }
        catch
        {
            // Aldrig låt en animation-incident lämna splash på skärmen — gå
            // vidare till app:en även om något oväntat händer.
        }
        finally
        {
            SwitchToMainApp();
        }
    }

    private async Task RunSplashAnimationAsync()
    {
        // Sammanlagd tid: 400 + 200 + 800 + 300 = 1700 ms. Inom 2–2.5 s-budgeten.
        // Fas 1 (400 ms): zoom in från 0 → 1.2 med fade in från 0 → 1, parallellt.
        await Task.WhenAll(
            ContentStack.ScaleToAsync(1.2, 400, Easing.SinOut),
            ContentStack.FadeToAsync(1, 400));
        // Fas 2 (200 ms): studsa tillbaka från 1.2 → 1.0.
        await ContentStack.ScaleToAsync(1.0, 200, Easing.SinIn);
        // Fas 3 (800 ms): håll så att användaren hinner se brand:en.
        await Task.Delay(800);
        // Fas 4 (300 ms): fade ut innan vi byter till app:en.
        await ContentStack.FadeToAsync(0, 300);
    }

    private void SwitchToMainApp()
    {
        // Byter Window.Page från denna SplashPage till AppShell. AppShell är
        // singleton i DI, så samma instans används för hela app-sessionen.
        // SplashPage detacheras och kan GC:as — den används bara vid kallstart,
        // inte vid resume från background.
        var window = Application.Current?.Windows.FirstOrDefault();
        if (window is null) return;
        window.Page = _shell;
    }
}
