using System;

namespace BdoClient.Services;

/// <summary>
/// In-memory tracker of which file fingerprint corresponds to the last successful
/// state resolution. It does NOT own scheduling and persists nothing.
/// </summary>
internal sealed class LocalFileChangeTracker
{
    private string? _path;
    private LocalizationFileFingerprint _baseline;
    private bool _hasBaseline;

    /// <summary>
    /// Records the fingerprint that was associated with a successful state resolution
    /// for the given path. Replaces any previous baseline, including a baseline for a
    /// different game root.
    /// </summary>
    public void CommitResolved(string path, LocalizationFileFingerprint fingerprint)
    {
        _path = path;
        _baseline = fingerprint;
        _hasBaseline = true;
    }

    public void Clear()
    {
        _path = null;
        _baseline = default;
        _hasBaseline = false;
    }

    /// <summary>
    /// True only when a baseline exists for exactly this path. A different game root
    /// never reuses the old baseline.
    /// </summary>
    public bool HasBaselineFor(string path)
    {
        if (!_hasBaseline || _path == null)
            return false;

        return string.Equals(_path, path, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns true only when a baseline exists for this exact path and the current
    /// fingerprint differs from it. With no baseline it returns false (never fabricates
    /// a change). With a different path it returns false (old baseline not reused).
    /// </summary>
    public bool HasChanged(string path, LocalizationFileFingerprint current)
    {
        if (!_hasBaseline || _path == null)
            return false;

        if (!string.Equals(_path, path, StringComparison.OrdinalIgnoreCase))
            return false;

        return !_baseline.Equals(current);
    }
}
