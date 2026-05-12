using Android.App;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.View;

namespace DoubleDashScore
{
    [Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
    public class MainActivity : MauiAppCompatActivity
    {
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
