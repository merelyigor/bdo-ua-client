namespace BdoClient.Services;

public sealed class LocalizationStateResult
{
    public LocalizationState State { get; }
    public string? Error { get; }
    public int? InstalledGamePatch { get; }
    public int? LocalGamePatch { get; }
    public LocalizationPatchTransition PatchTransition { get; }

    public LocalizationStateResult(
        LocalizationState state,
        string? error = null,
        int? installedGamePatch = null,
        int? localGamePatch = null,
        LocalizationPatchTransition patchTransition = LocalizationPatchTransition.None)
    {
        State = state;
        Error = error;
        InstalledGamePatch = installedGamePatch;
        LocalGamePatch = localGamePatch;
        PatchTransition = patchTransition;
    }

    public static LocalizationStateResult Success(LocalizationState state) => new(state);

    public static LocalizationStateResult WithWarning(LocalizationState state, string error) => new(state, error);

    public static LocalizationStateResult WithPatchTransition(
        LocalizationState state,
        int installedGamePatch,
        int localGamePatch,
        LocalizationPatchTransition transition,
        string? error = null) =>
        new(state, error, installedGamePatch, localGamePatch, transition);

    public static LocalizationStateResult WithManagedFileChanged(
        LocalizationState state,
        LocalizationPatchTransition transition,
        string? error = null) =>
        new(state, error, patchTransition: transition);
}

public enum LocalizationPatchTransition
{
    None,
    ExistingLocalizationOutdated,
    GameFileReplacedAfterPatch,
    ManagedFileChanged
}
