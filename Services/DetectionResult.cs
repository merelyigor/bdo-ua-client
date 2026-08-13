namespace BdoClient.Services;

public enum DetectionSource
{
    SavedConfig,
    Registry,
    Steam,
    ApiPattern,
    Manual
}

public sealed class DetectionResult
{
    public bool IsFound { get; }
    public string? GamePath { get; }
    public DetectionSource Source { get; }

    private DetectionResult(bool isFound, string? gamePath, DetectionSource source)
    {
        IsFound = isFound;
        GamePath = gamePath;
        Source = source;
    }

    public static DetectionResult Found(string gamePath, DetectionSource source) =>
        new(true, gamePath, source);

    public static DetectionResult NotFound() =>
        new(false, null, default);
}
