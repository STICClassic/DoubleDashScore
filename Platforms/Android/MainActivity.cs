using Android.App;
using Android.Content.PM;
using AndroidX.Core.View;

namespace DoubleDashScore
{
    [Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
    public class MainActivity : MauiAppCompatActivity
    {
        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            if (Window?.DecorView is not null)
            {
                // SetStatusBarColor är deprecated från API 35 (edge-to-edge per default),
                // men appen kör fortfarande mot lägre minSdk och behöver detta för att
                // matcha Shell-headerns mörka topbar. Edge-to-edge är en separat refaktor.
#pragma warning disable CA1422
                Window.SetStatusBarColor(Android.Graphics.Color.ParseColor("#1F1F1F"));
#pragma warning restore CA1422
                var controller = WindowCompat.GetInsetsController(Window, Window.DecorView);
                if (controller is not null)
                {
                    controller.AppearanceLightStatusBars = false;
                }
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
