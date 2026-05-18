using DoubleDashScore.Data;
using DoubleDashScore.Services;
using DoubleDashScore.ViewModels;
using DoubleDashScore.Views;
using Microsoft.Extensions.Logging;
using OxyPlot.Maui.Skia;
using SkiaSharp.Views.Maui.Controls.Hosting;

namespace DoubleDashScore;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseSkiaSharp()
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
        builder.Services.AddSingleton<HistoricalDataRepository>();
        builder.Services.AddSingleton<ExportService>();
        builder.Services.AddSingleton<ChartTransferStore>();
        builder.Services.AddSingleton<PhotoStorageService>();
        builder.Services.AddSingleton<OcrFlowContext>();
        builder.Services.AddSingleton<IApiKeyStore, SecureStorageApiKeyStore>();
        builder.Services.AddSingleton(_ => new HttpClient { Timeout = TimeSpan.FromSeconds(60) });
#if ANDROID
        builder.Services.AddSingleton<IOcrService, ClaudeVisionOcrService>();
#else
        builder.Services.AddSingleton<IOcrService, NoOpOcrService>();
#endif

        builder.Services.AddTransient<NightsListViewModel>();
        builder.Services.AddTransient<NewNightViewModel>();
        builder.Services.AddTransient<NightDetailViewModel>();
        builder.Services.AddTransient<RoundEntryViewModel>();
        builder.Services.AddTransient<PlayerEditViewModel>();
        builder.Services.AddTransient<NightStatsViewModel>();
        builder.Services.AddTransient<HistoryStatsViewModel>();
        builder.Services.AddTransient<FullScreenChartViewModel>();
        builder.Services.AddTransient<OcrCaptureViewModel>();
        builder.Services.AddTransient<OcrPreviewViewModel>();
        builder.Services.AddTransient<ApiKeySettingsViewModel>();

        builder.Services.AddTransient<NightsListPage>();
        builder.Services.AddTransient<NewNightPage>();
        builder.Services.AddTransient<NightDetailPage>();
        builder.Services.AddTransient<RoundEntryPage>();
        builder.Services.AddTransient<PlayerEditPage>();
        builder.Services.AddTransient<NightStatsPage>();
        builder.Services.AddTransient<HistoryStatsPage>();
        builder.Services.AddTransient<FullScreenChartPage>();
        builder.Services.AddTransient<OcrPreviewPage>();
        builder.Services.AddTransient<ApiKeySettingsPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
