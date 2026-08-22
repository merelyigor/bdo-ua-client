using BdoClient.Services;

namespace BdoClient.Tests.Services;

public sealed class LocalizationStatePresentationTests
{
    [Fact]
    public void GamePatchHashMismatch_DoesNotSayCorrupted()
    {
        var result = LocalizationStateResult.WithPatchTransition(
            LocalizationState.UpdateAvailable, 397, 398,
            LocalizationPatchTransition.GameFileReplacedAfterPatch,
            "Українська локалізація для патча 397 більше не активна.");

        var text = LocalizationStatePresentation.GetDisplayText(result);

        Assert.Equal("Після оновлення гри файл локалізації було замінено", text);
        Assert.DoesNotContain("пошкоджено", text);
    }

    [Fact]
    public void ExistingLocalizationPatchTransition_IsContextual()
    {
        var result = LocalizationStateResult.WithPatchTransition(
            LocalizationState.WaitingForRelease, 397, 398,
            LocalizationPatchTransition.ExistingLocalizationOutdated);

        Assert.Equal("Встановлена локалізація застаріла", LocalizationStatePresentation.GetDisplayText(result));
    }
}
