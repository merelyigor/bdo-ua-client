namespace BdoClient.Services;

public enum OperationState
{
    Idle,
    DetectingGame,
    LoadingApi,
    Downloading,
    Verifying,
    BackingUp,
    Installing,
    Restoring,
    Completed,
    Failed,
    Cancelled
}
