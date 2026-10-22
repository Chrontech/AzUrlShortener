namespace ChronicleMobile.App;

public sealed class App : Application
{
    public App(ActivationCoordinator activation)
    {
        Activation = activation;
        MainPage = new MainPage(activation);
    }

    public ActivationCoordinator Activation { get; }
}
