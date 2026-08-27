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

    [Fact]
    public void ManagedFileChanged_DoesNotSayCorrupted()
    {
        var result = LocalizationStateResult.WithManagedFileChanged(
            LocalizationState.UpdateAvailable,
            LocalizationPatchTransition.ManagedFileChanged,
            "Встановлена локалізація більше не активна.");

        var text = LocalizationStatePresentation.GetDisplayText(result);

        Assert.Equal("Встановлена локалізація більше не активна", text);
        Assert.DoesNotContain("пошкоджено", text);
        Assert.DoesNotContain("оновлення гри", text);
    }

    [Fact]
    public void ManagedFileChanged_WaitingForRelease_DoesNotSayCorrupted()
    {
        var result = LocalizationStateResult.WithManagedFileChanged(
            LocalizationState.WaitingForRelease,
            LocalizationPatchTransition.ManagedFileChanged,
            "Встановлена локалізація більше не активна. Актуальний українізатор ще не доступний.");

        var text = LocalizationStatePresentation.GetDisplayText(result);

        Assert.Equal("Встановлена локалізація більше не активна", text);
        Assert.DoesNotContain("пошкоджено", text);
    }
}
