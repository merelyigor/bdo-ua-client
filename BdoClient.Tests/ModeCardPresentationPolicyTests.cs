using BdoClient.Models;
using BdoClient.Services;

namespace BdoClient.Tests;

public sealed class ModeCardPresentationPolicyTests
{
    [Fact]
    public void NothingInstalled_CompatibleMode_OffersInstall()
    {
        var presentation = Create(LocalizationState.NotInstalled, null, null, Mode("full", "A"));
        Assert.Equal("Встановити", presentation.ActionText);
        Assert.True(presentation.ActionEnabled);
    }

    [Fact]
    public void ExactTarget_UpToDate_ShowsInstalledWithoutAction()
    {
        var presentation = Create(LocalizationState.UpToDate, "full", "A", Mode("full", "A"));
        Assert.True(presentation.IsInstalled);
        Assert.Equal("✓ Встановлено", presentation.StateText);
        Assert.Null(presentation.ActionText);
    }

    [Fact]
    public void SameModeNewRelease_OffersUpdate()
    {
        var presentation = Create(LocalizationState.UpdateAvailable, "full", "OLD", Mode("full", "NEW"));
        Assert.Equal("Оновити", presentation.ActionText);
        Assert.True(presentation.ActionEnabled);
    }

    [Fact]
    public void DifferentMode_OffersInstall()
    {
        var presentation = Create(LocalizationState.UpToDate, "full", "A", Mode("other", "B"));
        Assert.Equal("Встановити", presentation.ActionText);
    }

    [Fact]
    public void IncompatibleMode_HasNoAction()
    {
        var presentation = Create(LocalizationState.NotInstalled, null, null, Mode("full", "A"), CompatibilityResult.Blocked("patch mismatch"));
        Assert.Equal(ModeCardTone.Warning, presentation.Tone);
        Assert.False(presentation.ActionEnabled);
        Assert.Null(presentation.ActionText);
    }

    [Fact]
    public void MissingCurrentRelease_HasNoAction()
    {
        var presentation = Create(LocalizationState.NotInstalled, null, null, new LocalizationMode { Slug = "full" });
        Assert.Equal("Реліз недоступний", presentation.StateText);
        Assert.Null(presentation.ActionText);
    }

    [Fact]
    public void WaitingForRelease_UsesWarning()
    {
        var presentation = Create(LocalizationState.WaitingForRelease, "full", "OLD", Mode("full", "NEW"));
        Assert.Equal(ModeCardTone.Warning, presentation.Tone);
    }

    [Fact]
    public void CorruptedMode_IsNeverPresentedAsSuccess()
    {
        var presentation = Create(LocalizationState.Corrupted, "full", "OLD", Mode("full", "NEW"));
        Assert.Equal(ModeCardTone.Error, presentation.Tone);
        Assert.NotEqual("✓ Встановлено", presentation.StateText);
    }

    [Fact]
    public void Operation_DisablesActionsAndMarksTargetBusy()
    {
        var presentation = Create(LocalizationState.NotInstalled, null, null, Mode("full", "A"), operation: true, activeTarget: true);
        Assert.True(presentation.IsBusy);
        Assert.False(presentation.ActionEnabled);
    }

    private static ModeCardPresentation Create(LocalizationState state, string? installedSlug, string? installedId, LocalizationMode mode, CompatibilityResult? compatibility = null, bool operation = false, bool activeTarget = false) =>
        ModeCardPresentationPolicy.Create(state, installedSlug, installedId, mode, compatibility ?? CompatibilityResult.Allowed(), operation, activeTarget);

    private static LocalizationMode Mode(string slug, string id) => new()
    {
        Slug = slug,
        PublicName = slug,
        Current = new CurrentRelease { PublicId = id, Version = 1, Patch = 1, DownloadUrl = "https://example.com/file", Sha256 = "abc", SizeBytes = 1, CompatibleWithOfficialPatch = true }
    };
}
