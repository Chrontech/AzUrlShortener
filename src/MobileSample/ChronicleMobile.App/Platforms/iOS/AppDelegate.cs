using Foundation;
using UIKit;

namespace ChronicleMobile.App;

[Register("AppDelegate")]
public sealed class AppDelegate : MauiUIApplicationDelegate
{
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

    public override bool FinishedLaunching(UIApplication application, NSDictionary? launchOptions)
    {
        var launched = base.FinishedLaunching(application, launchOptions);
        if (launchOptions?[UIApplication.LaunchOptionsUrlKey] is NSUrl url)
        {
            ProcessUrl(url);
        }

        return launched;
    }

    public override bool OpenUrl(UIApplication app, NSUrl url, NSDictionary options)
    {
        ProcessUrl(url);
        return true;
    }

    private static void ProcessUrl(NSUrl url)
    {
        _ = ((App)Microsoft.Maui.Controls.Application.Current!).Activation.ProcessUriAsync(url.AbsoluteString);
    }
}
