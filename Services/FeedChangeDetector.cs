using BdoClient.Models;

namespace BdoClient.Services;

public static class FeedChangeDetector
{
    public static bool HasSemanticChange(ReleasesResponse? oldFeed, ReleasesResponse? newFeed)
    {
        if (oldFeed == null && newFeed == null) return false;
        if (oldFeed == null || newFeed == null) return true;

        var oldData = oldFeed.Data;
        var newData = newFeed.Data;
        if (oldData == null && newData == null) return false;
        if (oldData == null || newData == null) return true;

        if (oldData.OfficialPatch != newData.OfficialPatch) return true;
        if (!string.Equals(oldData.OfficialSourceUrl, newData.OfficialSourceUrl, StringComparison.Ordinal)) return true;

        var oldModes = oldData.Modes;
        var newModes = newData.Modes;

        if (oldModes == null && newModes == null) return false;
        if (oldModes == null || newModes == null) return true;

        if (oldModes.Count != newModes.Count) return true;

        var oldLookup = oldModes
            .Where(m => !string.IsNullOrEmpty(m.Slug))
            .ToDictionary(m => m.Slug!, m => m, StringComparer.Ordinal);

        foreach (var newMode in newModes)
        {
            if (string.IsNullOrEmpty(newMode.Slug)) continue;

            if (!oldLookup.TryGetValue(newMode.Slug, out var oldMode))
                return true;

            if (HasModeChange(oldMode, newMode))
                return true;
        }

        return false;
    }

    private static bool HasModeChange(LocalizationMode oldMode, LocalizationMode newMode)
    {
        if (!string.Equals(oldMode.PublicName, newMode.PublicName, StringComparison.Ordinal)) return true;
        if (!string.Equals(oldMode.Description, newMode.Description, StringComparison.Ordinal)) return true;
        if (!string.Equals(oldMode.Audience, newMode.Audience, StringComparison.Ordinal)) return true;

        var oldCurrent = oldMode.Current;
        var newCurrent = newMode.Current;

        if (oldCurrent == null && newCurrent == null) return false;
        if (oldCurrent == null || newCurrent == null) return true;

        if (!string.Equals(oldCurrent.PublicId, newCurrent.PublicId, StringComparison.Ordinal)) return true;
        if (oldCurrent.Version != newCurrent.Version) return true;
        if (oldCurrent.Patch != newCurrent.Patch) return true;
        if (oldCurrent.CompatibleWithOfficialPatch != newCurrent.CompatibleWithOfficialPatch) return true;
        if (oldCurrent.SizeBytes != newCurrent.SizeBytes) return true;
        if (!string.Equals(oldCurrent.Sha256, newCurrent.Sha256, StringComparison.Ordinal)) return true;
        if (!string.Equals(oldCurrent.DownloadUrl, newCurrent.DownloadUrl, StringComparison.Ordinal)) return true;
        if (!string.Equals(oldCurrent.PublishedAt, newCurrent.PublishedAt, StringComparison.Ordinal)) return true;

        return false;
    }
}
