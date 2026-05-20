#if ANDROID
using Android.Webkit;
using Android.Net.Http;
using Microsoft.Maui.Platform;

namespace AtemDirector.Mobile.Platforms.Android
{
    public class SslWebViewClient : MauiWebViewClient
    {
        public SslWebViewClient(Microsoft.Maui.Handlers.WebViewHandler handler) : base(handler)
        {
        }

        public override void OnReceivedSslError(global::Android.Webkit.WebView? view, SslErrorHandler? handler, SslError? error)
        {
            // For development/local server: proceed on SSL errors (self-signed certs)
            // In a production app, you would add more validation logic here.
            handler?.Proceed();
        }
    }
}
#endif
