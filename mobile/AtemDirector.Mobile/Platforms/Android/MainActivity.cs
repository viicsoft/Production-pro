using Android.App;
using Android.Content.PM;
using Android.OS;

namespace AtemDirector.Mobile;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        
        // Request runtime permissions for camera and microphone
        RequestPermissions();
    }

    private void RequestPermissions()
    {
        // Check if we need to request permissions (Android 6.0+)
        if (Build.VERSION.SdkInt >= BuildVersionCodes.M)
        {
            var permissionList = new List<string>
            {
                global::Android.Manifest.Permission.Camera,
                global::Android.Manifest.Permission.RecordAudio,
                global::Android.Manifest.Permission.ModifyAudioSettings
            };

            if (Build.VERSION.SdkInt >= BuildVersionCodes.S)
            {
                permissionList.Add(global::Android.Manifest.Permission.BluetoothConnect);
                permissionList.Add(global::Android.Manifest.Permission.BluetoothScan);
            }

            var permissions = permissionList.ToArray();

            // Request permissions if not already granted
            bool needsRequest = false;
            foreach (var permission in permissions)
            {
                if (CheckSelfPermission(permission) != Permission.Granted)
                {
                    needsRequest = true;
                    break;
                }
            }

            if (needsRequest)
            {
                RequestPermissions(permissions, 0);
            }
        }
    }
}
