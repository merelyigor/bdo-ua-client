using BdoClient.Services;

namespace BdoClient.Tests.Services;

public sealed class GamePatchPresentationPolicyTests
{
    [Fact]
    public void OlderInstalledPatch_ShowsUpdateGameWarningWithBothPatches()
    {
        var presentation = GamePatchPresentationPolicy.Create(null, 399, 401);

        Assert.Equal(GamePatchStatus.Outdated, presentation.Status);
        Assert.Equal(
            $"⚠ Потрібно оновити гру{Environment.NewLine}Встановлено: patch 399 • актуальний: patch 401",
            presentation.Text);
    }

    [Fact]
    public void MatchingInstalledPatch_PreservesPositiveStatus()
    {
        var presentation = GamePatchPresentationPolicy.Create(null, 401, 401);

        Assert.Equal(GamePatchStatus.Current, presentation.Status);
        Assert.Equal("✓ Гру знайдено • patch 401", presentation.Text);
    }

    [Fact]
    public void ManualDetection_PreservesManualStatus()
    {
        var presentation = GamePatchPresentationPolicy.Create(DetectionSource.Manual, 401, 401);

        Assert.Equal("✓ Гру знайдено вручну • patch 401", presentation.Text);
    }

    [Fact]
    public void MissingLatestPatch_KeepsDetectedGameStatusWithoutWarning()
    {
        var presentation = GamePatchPresentationPolicy.Create(null, 399, null);

        Assert.Equal(GamePatchStatus.Unknown, presentation.Status);
        Assert.Equal("✓ Гру знайдено • patch 399", presentation.Text);
    }

    [Fact]
    public void MissingInstalledPatch_KeepsDetectedGameStatusWithoutPatch()
    {
        var presentation = GamePatchPresentationPolicy.Create(null, null, 401);

        Assert.Equal(GamePatchStatus.Unknown, presentation.Status);
        Assert.Equal("✓ Гру знайдено", presentation.Text);
    }
}
