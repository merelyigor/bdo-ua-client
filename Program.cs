using System.Reflection;
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

        ILogger logger = new FileLogger(appPaths.LogsDir);
        var configStore = new ConfigStore(appPaths, logger);
        var stateStore = new InstallationStateStore(appPaths, logger);
        var httpClient = new HttpClient();
        httpClient.Timeout = TimeSpan.FromSeconds(30);

        var apiClient = new Api.BdoUaApiClient(httpClient, logger);
        var localizationInstaller = new LocalizationInstaller(httpClient, appPaths, logger);
        var backupStore = new BackupStore(appPaths, logger);
        var gameDetector = new GameDetector(configStore, logger);
        var stateService = new LocalizationStateService(stateStore, logger);
        var compatService = new LocalizationCompatibilityService();

        var version = Assembly.GetEntryAssembly()?
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion ?? "unknown";
        logger.Info($"Application started. version={version}");

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        try
        {
            Application.Run(new MainForm(
                configStore, apiClient, gameDetector,
                stateService, compatService,
                localizationInstaller, backupStore, stateStore, logger));
        }
        catch (Exception ex)
        {
            logger.Error($"Unhandled application exception: {ex.Message}");
            throw;
        }
        finally
        {
            logger.Info("Application exited.");
        }
    }
}
