using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;

namespace ChronicleMobile.App;

[Activity(
    Theme = "@style/Maui.SplashTheme",
    MainLauncher = true,
    Exported = true,
    LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
[IntentFilter(
    [Intent.ActionView],
    Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable],
    DataScheme = "ChronicleMobile")]
public sealed class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        ProcessIntent(Intent);
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        ProcessIntent(intent);
    }

    private static void ProcessIntent(Intent? intent)
    {
        if (intent?.Action == Intent.ActionView && intent.DataString is { } uri)
        {
            _ = ((App)Microsoft.Maui.Controls.Application.Current!).Activation.ProcessUriAsync(uri);
        }
    }
}
