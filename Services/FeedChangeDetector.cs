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

        // Mode ordering is semantic — compare ordered slug sequences
        var oldSlugs = oldModes.Select(m => m.Slug ?? "").ToList();
        var newSlugs = newModes.Select(m => m.Slug ?? "").ToList();
        for (int i = 0; i < oldSlugs.Count; i++)
        {
            if (!string.Equals(oldSlugs[i], newSlugs[i], StringComparison.Ordinal))
                return true;
        }

        // Per-mode field comparison — use list index since order is already verified
        for (int i = 0; i < oldModes.Count; i++)
        {
            if (HasModeChange(oldModes[i], newModes[i]))
                return true;
        }

        return false;
    }

    private static bool HasModeChange(LocalizationMode oldMode, LocalizationMode newMode)
    {
        if (!string.Equals(oldMode.Slug ?? "", newMode.Slug ?? "", StringComparison.Ordinal)) return true;
        if (!string.Equals(oldMode.PublicName ?? "", newMode.PublicName ?? "", StringComparison.Ordinal)) return true;
        if (!string.Equals(oldMode.Description ?? "", newMode.Description ?? "", StringComparison.Ordinal)) return true;
        if (!string.Equals(oldMode.Audience ?? "", newMode.Audience ?? "", StringComparison.Ordinal)) return true;

        var oldCurrent = oldMode.Current;
        var newCurrent = newMode.Current;

        if (oldCurrent == null && newCurrent == null) return false;
        if (oldCurrent == null || newCurrent == null) return true;

        if (!string.Equals(oldCurrent.PublicId ?? "", newCurrent.PublicId ?? "", StringComparison.Ordinal)) return true;
        if (oldCurrent.Version != newCurrent.Version) return true;
        if (oldCurrent.Patch != newCurrent.Patch) return true;
        if (oldCurrent.CompatibleWithOfficialPatch != newCurrent.CompatibleWithOfficialPatch) return true;
        if (oldCurrent.SizeBytes != newCurrent.SizeBytes) return true;
        if (!string.Equals(oldCurrent.Sha256 ?? "", newCurrent.Sha256 ?? "", StringComparison.Ordinal)) return true;
        if (!string.Equals(oldCurrent.DownloadUrl ?? "", newCurrent.DownloadUrl ?? "", StringComparison.Ordinal)) return true;
        if (!string.Equals(oldCurrent.PublishedAt ?? "", newCurrent.PublishedAt ?? "", StringComparison.Ordinal)) return true;

        return false;
    }
}
