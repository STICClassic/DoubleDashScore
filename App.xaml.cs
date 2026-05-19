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
            // AppShell tar AppShellViewModel via DI för flyout-menyns kommandon
            // och aktiv-route-tracking. Måste resolvas via service provider —
            // `new AppShell()` skulle gå förbi DI.
            return new Window(_services.GetRequiredService<AppShell>());
        }
    }
}