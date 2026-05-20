#if ANDROID
using Android.Webkit;

namespace AtemDirector.Mobile.Platforms.Android
{
    public class PermissionWebChromeClient : Microsoft.Maui.Platform.MauiWebChromeClient
    {
        public PermissionWebChromeClient(Microsoft.Maui.Handlers.IWebViewHandler handler) : base(handler)
        {
        }

        public override void OnPermissionRequest(PermissionRequest? request)
        {
            if (request == null) return;

            var resources = request.GetResources();
            if (resources == null) return;

            // Grant all requested resources in a single call
            request.Grant(resources);
        }
    }
}
#endif
