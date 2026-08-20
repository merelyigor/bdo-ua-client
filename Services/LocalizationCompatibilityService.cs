using BdoClient.Models;

namespace BdoClient.Services;

public sealed class LocalizationCompatibilityService
{
    public CompatibilityResult Check(CurrentRelease? current)
    {
        if (current == null)
            return CompatibilityResult.Blocked("Актуальний українізатор ще не доступний. Перевірте оновлення пізніше.");

        if (string.IsNullOrWhiteSpace(current.PublicId))
            return CompatibilityResult.Blocked("Актуальний українізатор ще не доступний. Перевірте оновлення пізніше.");

        if (!current.CompatibleWithOfficialPatch)
            return CompatibilityResult.Blocked("Цей українізатор не сумісний з поточним патчем гри.");

        return CompatibilityResult.Allowed();
    }
}
