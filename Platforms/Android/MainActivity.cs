using Android.App;
using Android.Content.PM;
using AndroidX.Core.View;

namespace DoubleDashScore
{
    [Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
    public class MainActivity : MauiAppCompatActivity
    {
        // Tidigare OnCreate-override satte AppearanceLightStatusBars via
        // WindowCompat.GetInsetsController och styrde statusbar-färgen via theme + runtime.
        // På Android 15 (API 35) kraschade det här flödet vid uppstart. Vi accepterar
        // Androids default-hantering av statusbar och låter MauiAppCompatActivity:s egen
        // OnCreate köras orörd.

        // EnterFullscreen/ExitFullscreen rör inte statusbar-färgen; de döljer/visar
        // bara system bars för riktig kant-till-kant i FullScreenChartPage. Inget krasch-
        // beteende observerat där, så de behålls.
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
