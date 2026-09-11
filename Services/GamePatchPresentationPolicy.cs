namespace BdoClient.Services;

internal enum GamePatchStatus
{
    Unknown,
    Current,
    Outdated,
    NewerThanLatestLocalization
}

internal sealed record GamePatchPresentation(GamePatchStatus Status, string Text);

internal static class GamePatchPresentationPolicy
{
    public static GamePatchPresentation Create(
        DetectionSource? source,
        int? installedPatch,
        int? latestKnownPatch,
        int? latestKnownLocalizationPatch = null)
    {
        var foundText = source == DetectionSource.Manual
            ? "✓ Гру знайдено вручну"
            : "✓ Гру знайдено";

        if (installedPatch is > 0 && latestKnownLocalizationPatch is > 0
            && installedPatch.Value > latestKnownLocalizationPatch.Value)
        {
            return new(
                GamePatchStatus.NewerThanLatestLocalization,
                $"⚠ Гра новіша за доступну локалізацію{Environment.NewLine}Встановлено: patch {installedPatch.Value} • локалізація: patch {latestKnownLocalizationPatch.Value}");
        }

        if (installedPatch is > 0 && latestKnownPatch is > 0
            && installedPatch.Value < latestKnownPatch.Value)
        {
            return new(
                GamePatchStatus.Outdated,
                $"⚠ Потрібно оновити гру{Environment.NewLine}Встановлено: patch {installedPatch.Value} • актуальний: patch {latestKnownPatch.Value}");
        }

        if (installedPatch is > 0)
        {
            var status = latestKnownPatch is > 0
                ? GamePatchStatus.Current
                : GamePatchStatus.Unknown;
            return new(status, $"{foundText} • patch {installedPatch.Value}");
        }

        return new(GamePatchStatus.Unknown, foundText);
    }
}
