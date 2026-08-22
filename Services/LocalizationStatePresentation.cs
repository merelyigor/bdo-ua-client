namespace BdoClient.Services;

public static class LocalizationStatePresentation
{
    public static string GetDisplayText(LocalizationStateResult result) => result.PatchTransition switch
    {
        LocalizationPatchTransition.ExistingLocalizationOutdated => "Встановлена локалізація застаріла",
        LocalizationPatchTransition.GameFileReplacedAfterPatch => "Після оновлення гри файл локалізації було замінено",
        _ => result.State switch
        {
            LocalizationState.NotInstalled => "Локалізацію не встановлено",
            LocalizationState.UpToDate => "✓ Локалізація актуальна",
            LocalizationState.UpdateAvailable => "Доступне оновлення локалізації",
            LocalizationState.WaitingForRelease => "Очікується актуальна версія локалізації",
            LocalizationState.InstalledVersionUnknown => "Не вдалося визначити версію локалізації",
            LocalizationState.Corrupted => "Файл локалізації пошкоджено",
            _ => "Не визначено"
        }
    };
}
