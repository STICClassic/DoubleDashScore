using DoubleDashScore.Views;
using Microsoft.Extensions.DependencyInjection;

namespace DoubleDashScore
{
    public partial class App : Application
    {
        private readonly IServiceProvider _services;

        public App(IServiceProvider services)
        {
            InitializeComponent();
            _services = services;
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            // Splash visas som Window.Page först. När animationen är klar byter
            // SplashPage själv ut Window.Page mot AppShell. Att starta i Shell
            // skulle visa NightsListPage direkt utan splash; att lägga
            // SplashPage som ShellContent skulle göra den till en navigerbar
            // route som kunde dyka upp via back-stack — vi vill att den bara
            // existerar precis vid kallstart.
            return new Window(_services.GetRequiredService<SplashPage>());
        }
    }
}