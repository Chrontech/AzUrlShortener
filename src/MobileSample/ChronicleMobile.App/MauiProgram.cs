using ChronicleMobile.Core;

namespace ChronicleMobile.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();
        builder.Services.AddSingleton(new HttpClient());
        builder.Services.AddSingleton<ResolverClient>();
        builder.Services.AddSingleton<ActivationCoordinator>();

        return builder.Build();
    }
}
