using ChronicleMobile.Core;

namespace ChronicleMobile.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();
        builder.Services.AddSingleton(new HttpClient());
#if ANDROID && DEBUG
        // Local testing requires adb reverse tcp:7071 tcp:7071; production uses https://short.gochronicle.com/.
        builder.Services.AddSingleton(services => new ResolverClient(
            services.GetRequiredService<HttpClient>(),
            new Uri("http://127.0.0.1:7071/")));
#else
        builder.Services.AddSingleton<ResolverClient>();
#endif
        builder.Services.AddSingleton<ActivationCoordinator>();

        return builder.Build();
    }
}
