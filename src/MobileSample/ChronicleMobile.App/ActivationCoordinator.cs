using System.ComponentModel;
using ChronicleMobile.Core;

namespace ChronicleMobile.App;

public sealed class ActivationCoordinator(ResolverClient resolverClient) : INotifyPropertyChanged
{
    private ResolutionResult result = ResolutionResult.Normal();
    private bool isLoading;
    private int activationGeneration;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ResolutionResult Result
    {
        get => result;
        private set
        {
            result = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Result)));
        }
    }

    public bool IsLoading
    {
        get => isLoading;
        private set
        {
            isLoading = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsLoading)));
        }
    }

    public async Task ProcessUriAsync(string? rawUri)
    {
        var generation = Interlocked.Increment(ref activationGeneration);
        IsLoading = true;
        try
        {
            var resolved = await resolverClient.ResolveAsync(rawUri);
            if (generation == Volatile.Read(ref activationGeneration))
            {
                Result = resolved;
            }
        }
        finally
        {
            if (generation == Volatile.Read(ref activationGeneration))
            {
                IsLoading = false;
            }
        }
    }
}
