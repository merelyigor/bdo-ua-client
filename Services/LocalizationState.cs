namespace BdoClient.Services;

public enum LocalizationState
{
    NotInstalled,
    UpToDate,
    UpdateAvailable,
    WaitingForRelease,
    InstalledVersionUnknown,
    Corrupted
}
