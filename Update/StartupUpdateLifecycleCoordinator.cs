namespace BdoClient.Update;

internal static class StartupUpdateLifecycleCoordinator
{
    public static async Task RunAsync(
        Func<Task> maintenance,
        Func<bool> isClosing,
        Action startDiscovery,
        Action<Exception> reportFailure)
    {
        try
        {
            await maintenance().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            reportFailure(ex);
        }

        if (!isClosing())
            startDiscovery();
    }
}
