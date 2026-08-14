using System.Windows.Forms;
using BdoClient.Logging;
using BdoClient.Services;
using BdoClient.Storage;

namespace BdoClient;

static class Program
{
    [STAThread]
    static void Main()
    {
        var appPaths = new AppPaths();
        appPaths.EnsureDirectories();

        ILogger logger = new SimpleLogger();
        var configStore = new ConfigStore(appPaths, logger);
        var stateStore = new InstallationStateStore(appPaths, logger);
        var httpClient = new HttpClient();
        var apiClient = new Api.BdoUaApiClient(httpClient, logger);
        var gameDetector = new GameDetector(configStore, logger);
        var stateService = new LocalizationStateService(stateStore, logger);
        var compatService = new LocalizationCompatibilityService();

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm(
            appPaths, configStore, stateStore, apiClient,
            gameDetector, stateService, compatService, logger));
    }
}

internal sealed class SimpleLogger : ILogger
{
    public void Debug(string message) { }
    public void Info(string message) { }
    public void Warning(string message) => Console.Error.WriteLine($"[WARN] {message}");
    public void Error(string message) => Console.Error.WriteLine($"[ERROR] {message}");
}
