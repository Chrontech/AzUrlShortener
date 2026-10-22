using Foundation;
using UIKit;
using ChronicleMobile.Core;

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
        else if (application.UserActivity is { } userActivity)
        {
            TryProcessUserActivity(userActivity);
        }

        return launched;
    }

    public override bool OpenUrl(UIApplication app, NSUrl url, NSDictionary options)
    {
        ProcessUrl(url);
        return true;
    }

    public override bool ContinueUserActivity(
        UIApplication application,
        NSUserActivity userActivity,
        UIApplicationRestorationHandler completionHandler)
        => TryProcessUserActivity(userActivity);

    private static bool TryProcessUserActivity(NSUserActivity userActivity)
    {
        if (userActivity.ActivityType != NSUserActivityType.BrowsingWeb
            || userActivity.WebPageUrl is not { } url
            || !DeepLinkParser.TryParse(url.AbsoluteString, out _))
        {
            return false;
        }

        ProcessUrl(url);
        return true;
    }

    private static void ProcessUrl(NSUrl url)
    {
        _ = ((App)Microsoft.Maui.Controls.Application.Current!).Activation.ProcessUriAsync(url.AbsoluteString);
    }
}
