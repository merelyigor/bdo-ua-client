namespace BdoClient.Services;

public static class LocalizationStatePresentation
{
    public static string GetDisplayText(LocalizationStateResult result) => result.PatchTransition switch
    {
        LocalizationPatchTransition.ExistingLocalizationOutdated => "Встановлена локалізація застаріла",
        LocalizationPatchTransition.GameFileReplacedAfterPatch => "Гра оновила файл локалізації",
        _ => result.State switch
        {
            LocalizationState.NotInstalled => "Локалізацію не встановлено",
            LocalizationState.UpToDate => "✓ Встановлена локалізація актуальна",
            LocalizationState.UpdateAvailable => "Доступна новіша версія встановленої локалізації",
            LocalizationState.WaitingForRelease => "Очікується актуальний реліз",
            LocalizationState.InstalledVersionUnknown => "Не вдалося визначити встановлену версію",
            LocalizationState.Corrupted => "Файл локалізації пошкоджено",
            _ => "Не визначено"
        }
    };
}
