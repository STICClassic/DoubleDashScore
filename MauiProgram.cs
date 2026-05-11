using DoubleDashScore.Data;
using DoubleDashScore.ViewModels;
using DoubleDashScore.Views;
using Microsoft.Extensions.Logging;
using OxyPlot.Maui.Skia;

namespace DoubleDashScore;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseOxyPlotSkia()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        builder.Services.AddSingleton<DatabaseService>();
        builder.Services.AddSingleton<PlayerRepository>();
        builder.Services.AddSingleton<GameNightRepository>();
        builder.Services.AddSingleton<RoundRepository>();

        builder.Services.AddTransient<NightsListViewModel>();
        builder.Services.AddTransient<NewNightViewModel>();
        builder.Services.AddTransient<NightDetailViewModel>();
        builder.Services.AddTransient<RoundEntryViewModel>();
        builder.Services.AddTransient<PlayerEditViewModel>();
        builder.Services.AddTransient<NightStatsViewModel>();
        builder.Services.AddTransient<HistoryStatsViewModel>();

        builder.Services.AddTransient<NightsListPage>();
        builder.Services.AddTransient<NewNightPage>();
        builder.Services.AddTransient<NightDetailPage>();
        builder.Services.AddTransient<RoundEntryPage>();
        builder.Services.AddTransient<PlayerEditPage>();
        builder.Services.AddTransient<NightStatsPage>();
        builder.Services.AddTransient<HistoryStatsPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
