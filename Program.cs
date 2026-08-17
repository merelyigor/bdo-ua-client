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

        // Pre-warm HttpClient: first request to a new endpoint can take ~20s due to
        // TLS/CRL/OCSP cold start. Fire-and-forget warmup so TLS session is cached
        // by the time MainForm_Shown calls the API.
        var warmupTask = Task.Run(async () =>
        {
            try
            {
                using var warmup = new HttpRequestMessage(HttpMethod.Head, "https://bdo-ua.com.ua/api/public/v1/releases");
                await httpClient.SendAsync(warmup);
            }
            catch { /* best-effort */ }
        });

        var apiClient = new Api.BdoUaApiClient(httpClient, logger, warmupTask);
        var localizationInstaller = new LocalizationInstaller(httpClient, appPaths, logger);
        var backupStore = new BackupStore(appPaths, logger);
        var gameDetector = new GameDetector(configStore, logger);
        var stateService = new LocalizationStateService(stateStore, logger);
        var compatService = new LocalizationCompatibilityService();

        logger.Info("Application started.");

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
