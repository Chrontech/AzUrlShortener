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
        // Production must use https://short.gochronicle.com/.
        var resolverBaseUri = new Uri("http://10.0.2.2:7071/");
#else
        var resolverBaseUri = new Uri("https://short.gochronicle.com/");
#endif
        builder.Services.AddSingleton(services => new ResolverClient(services.GetRequiredService<HttpClient>(), resolverBaseUri));
        builder.Services.AddSingleton<ActivationCoordinator>();

        return builder.Build();
    }
}
