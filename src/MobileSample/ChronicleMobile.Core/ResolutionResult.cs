namespace ChronicleMobile.Core;

public enum ResolutionStatus
{
    Normal,
    InvalidUri,
    Resolved,
    Missing,
    Archived,
    Error
}

public sealed record ResolutionResult(
    ResolutionStatus Status,
    string? ShortId = null,
    IReadOnlyDictionary<string, string>? Metadata = null)
{
    public static ResolutionResult Normal() => new(ResolutionStatus.Normal);

    public static ResolutionResult InvalidUri() => new(ResolutionStatus.InvalidUri);

    public static ResolutionResult Error(string shortId) => new(ResolutionStatus.Error, shortId);
}
