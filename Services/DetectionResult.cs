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
    public DetectionSource? Source { get; }
    public bool Persisted { get; }

    private DetectionResult(bool isFound, string? gamePath, DetectionSource? source, bool persisted)
    {
        IsFound = isFound;
        GamePath = gamePath;
        Source = source;
        Persisted = persisted;
    }

    public static DetectionResult Found(string gamePath, DetectionSource source, bool persisted = true) =>
        new(true, gamePath, source, persisted);

    public static DetectionResult NotFound() =>
        new(false, null, null, false);
}
