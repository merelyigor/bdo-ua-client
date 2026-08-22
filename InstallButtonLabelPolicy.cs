using BdoClient.Services;

namespace BdoClient;

internal static class InstallButtonLabelPolicy
{
    public const string InstallText = "Встановити";
    public const string UpdateText = "Оновити";

    public static string GetText(
        LocalizationState factualState,
        bool hasInstalledApiState,
        bool sameInstalledModeSelected,
        bool exactSelectedTarget)
    {
        return factualState == LocalizationState.UpdateAvailable
            && hasInstalledApiState
            && sameInstalledModeSelected
            && !exactSelectedTarget
            ? UpdateText
            : InstallText;
    }
}
