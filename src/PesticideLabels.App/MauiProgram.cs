using Microsoft.Extensions.Logging;

namespace PesticideLabels.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

#if ANDROID
        // The label viewer shows pages saved in the app's own cache folder and needs pinch zoom.
        Microsoft.Maui.Handlers.WebViewHandler.Mapper.AppendToMapping("LabelViewer", (handler, _) =>
        {
            var settings = handler.PlatformView.Settings;
            settings.AllowFileAccess = true;
            settings.JavaScriptEnabled = false;
            settings.SetSupportZoom(true);
            settings.BuiltInZoomControls = true;
            settings.DisplayZoomControls = false;
            settings.LoadWithOverviewMode = true;
            settings.UseWideViewPort = true;
        });
#endif

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
