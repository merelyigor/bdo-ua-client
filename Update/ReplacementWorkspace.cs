using BdoClient.Storage;

namespace BdoClient.Update;

internal sealed class ReplacementWorkspace
{
    private const string FallbackDirectoryName = ".bdo-ua-client-update";

    public string DirectoryPath { get; }
    public string CandidatePath { get; }
    public string BackupPath { get; }
    public string FailedNewPath { get; }
    public bool UsesTargetVolumeFallback { get; }

    private ReplacementWorkspace(string directoryPath, bool usesTargetVolumeFallback)
    {
        DirectoryPath = directoryPath;
        UsesTargetVolumeFallback = usesTargetVolumeFallback;
        CandidatePath = Path.Combine(directoryPath, "candidate.new");
        BackupPath = Path.Combine(directoryPath, "original.bak");
        FailedNewPath = Path.Combine(directoryPath, "failed-new");
    }

    public static ReplacementWorkspace Derive(AppPaths appPaths, string sessionId, string targetPath)
    {
        ArgumentNullException.ThrowIfNull(appPaths);
        var normalizedSessionId = UpdateSessionStore.NormalizeSessionId(sessionId);
        var normalizedTarget = Path.GetFullPath(targetPath);
        var targetDirectory = Path.GetDirectoryName(normalizedTarget)
            ?? throw new ArgumentException("Target path must have a directory", nameof(targetPath));
        var appDataSession = Path.GetFullPath(Path.Combine(appPaths.UpdatesDir, normalizedSessionId));

        if (string.Equals(Path.GetPathRoot(appDataSession), Path.GetPathRoot(normalizedTarget), StringComparison.OrdinalIgnoreCase))
            return new ReplacementWorkspace(appDataSession, usesTargetVolumeFallback: false);

        var fallbackRoot = Path.GetFullPath(Path.Combine(targetDirectory, FallbackDirectoryName));
        var fallbackSession = Path.GetFullPath(Path.Combine(fallbackRoot, normalizedSessionId));
        EnsureWithin(fallbackSession, targetDirectory);
        if (!string.Equals(Path.GetPathRoot(fallbackSession), Path.GetPathRoot(normalizedTarget), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Replacement fallback workspace is not on the target volume");

        return new ReplacementWorkspace(fallbackSession, usesTargetVolumeFallback: true);
    }

    public void EnsureDirectory()
    {
        Directory.CreateDirectory(DirectoryPath);
        if (UsesTargetVolumeFallback)
        {
            var attributes = File.GetAttributes(DirectoryPath);
            File.SetAttributes(DirectoryPath, attributes | FileAttributes.Hidden);
            var fallbackRoot = Path.GetDirectoryName(DirectoryPath);
            if (fallbackRoot != null && Directory.Exists(fallbackRoot))
            {
                var rootAttributes = File.GetAttributes(fallbackRoot);
                File.SetAttributes(fallbackRoot, rootAttributes | FileAttributes.Hidden);
            }
        }
    }

    public bool TryDeleteOwnedFallbackWorkspace()
    {
        if (!UsesTargetVolumeFallback || !Directory.Exists(DirectoryPath))
            return true;

        if (Directory.EnumerateFileSystemEntries(DirectoryPath).Any())
            return false;

        Directory.Delete(DirectoryPath);
        var parent = Path.GetDirectoryName(DirectoryPath);
        if (parent != null && Directory.Exists(parent) && !Directory.EnumerateFileSystemEntries(parent).Any())
            Directory.Delete(parent);
        return true;
    }

    private static void EnsureWithin(string path, string parent)
    {
        var normalizedPath = Path.GetFullPath(path);
        var normalizedParent = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!normalizedPath.StartsWith(normalizedParent, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Replacement workspace escapes target directory");
    }
}
