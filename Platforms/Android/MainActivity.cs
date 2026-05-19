using Android.App;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.View;

namespace DoubleDashScore
{
    [Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
    public class MainActivity : MauiAppCompatActivity
    {
        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            if (Window?.DecorView is null) return;

            // 6a:s SetStatusBarColor räcker inte ensam när target SDK ≥ 35 — Android 15+
            // enforce:ar edge-to-edge och gör statusbar transparent, vilket gör att
            // colorPrimary (= orange #FB923C) lyser igenom från splash- och Maui.MainTheme.
            //
            // Tre lager för att täcka API 21 → 36 konsekvent:
            //  1) styles.xml överskuggar Maui.SplashTheme och Maui.MainTheme med
            //     android:statusBarColor=#1F1F1F + windowLightStatusBar=false.
            //  2) WindowCompat.SetDecorFitsSystemWindows(true) opt:ar ut ur edge-to-edge
            //     så statusbar:n förblir opak (deprecated på API 35+ men fungerar
            //     fortfarande som escape hatch).
            //  3) Window.SetStatusBarColor sätter färgen i runtime för säkerhets skull.
            // Edge-to-edge som första klassens lösning (rita själva under transparent
            // statusbar) ligger utanför scope för Skiva 6.
#pragma warning disable CA1422
            WindowCompat.SetDecorFitsSystemWindows(Window, true);
            Window.SetStatusBarColor(Android.Graphics.Color.ParseColor("#1F1F1F"));
#pragma warning restore CA1422
            var controller = WindowCompat.GetInsetsController(Window, Window.DecorView);
            if (controller is not null)
            {
                controller.AppearanceLightStatusBars = false;
            }
        }


        // Anropas av FullScreenChartPage.OnAppearing för riktig kant-till-kant.
        // Använder WindowInsetsControllerCompat (API 21+, MAUI 10 min är API 24).
        public void EnterFullscreen()
        {
            if (Window?.DecorView == null) return;
            var controller = WindowCompat.GetInsetsController(Window, Window.DecorView);
            if (controller == null) return;
            controller.Hide(WindowInsetsCompat.Type.SystemBars());
            controller.SystemBarsBehavior = WindowInsetsControllerCompat.BehaviorShowTransientBarsBySwipe;
        }

        public void ExitFullscreen()
        {
            if (Window?.DecorView == null) return;
            var controller = WindowCompat.GetInsetsController(Window, Window.DecorView);
            controller?.Show(WindowInsetsCompat.Type.SystemBars());
        }
    }
}
