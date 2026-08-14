using BdoClient.Models;

namespace BdoClient.Services;

public sealed class LocalizationCompatibilityService
{
    public CompatibilityResult Check(CurrentRelease? current)
    {
        if (current == null)
            return CompatibilityResult.Blocked("Current release is not available.");

        if (string.IsNullOrWhiteSpace(current.PublicId))
            return CompatibilityResult.Blocked("Current release metadata is invalid: public_id is empty.");

        if (!current.CompatibleWithOfficialPatch)
            return CompatibilityResult.Blocked("Release is not compatible with the current official game patch.");

        return CompatibilityResult.Allowed();
    }
}
