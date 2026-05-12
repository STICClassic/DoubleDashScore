using DoubleDashScore.Views;

namespace DoubleDashScore;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();

        Routing.RegisterRoute("NewNightPage", typeof(NewNightPage));
        Routing.RegisterRoute("NightDetailPage", typeof(NightDetailPage));
        Routing.RegisterRoute("RoundEntryPage", typeof(RoundEntryPage));
        Routing.RegisterRoute("PlayerEditPage", typeof(PlayerEditPage));
        Routing.RegisterRoute("NightStatsPage", typeof(NightStatsPage));
        Routing.RegisterRoute("HistoryStatsPage", typeof(HistoryStatsPage));
        Routing.RegisterRoute("FullScreenChartPage", typeof(FullScreenChartPage));
    }
}
