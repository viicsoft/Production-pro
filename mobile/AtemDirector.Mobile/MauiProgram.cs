using Microsoft.Extensions.Logging;
using CommunityToolkit.Maui;

namespace AtemDirector.Mobile;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.UseMauiCommunityToolkit()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});

		// Add custom SSL and Permission handling for WebView on Android
		Microsoft.Maui.Handlers.WebViewHandler.Mapper.AppendToMapping("WebViewExtensions", (handler, view) =>
		{
#if ANDROID
			// Handle SSL errors for self-signed certs
			handler.PlatformView.SetWebViewClient(new AtemDirector.Mobile.Platforms.Android.SslWebViewClient((Microsoft.Maui.Handlers.WebViewHandler)handler));
			
			// Handle hardware permissions (Microphone/Camera)
			handler.PlatformView.SetWebChromeClient(new AtemDirector.Mobile.Platforms.Android.PermissionWebChromeClient(handler));
#endif
		});

#if DEBUG
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
