using System.Windows.Forms;
using BdoClient.Logging;
using BdoClient.Services;
using BdoClient.Storage;
using BdoClient.Update;

namespace BdoClient;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        var commandLine = ApplicationCommandLine.Parse(args);

        switch (commandLine.Mode)
        {
            case CommandLineMode.ApplyUpdate:
                RunHelperMode(commandLine.ApplyUpdateSessionId!);
                return;

            case CommandLineMode.InvalidApplyUpdate:
                Console.Error.WriteLine("Invalid --apply-update arguments. Expected: --apply-update <session-id>");
                Environment.Exit(ApplicationCommandLine.ExitCodeInvalidArgs);
                return;

            case CommandLineMode.Normal:
            default:
                RunNormalMode();
                return;
        }
    }

    private static void RunHelperMode(string sessionId)
    {
        var paths = new AppPaths();
        paths.EnsureDirectories();
        ILogger log = new FileLogger(paths.LogsDir);

        log.Info($"Self-update helper mode: session={sessionId}");
        var sessionStore = new UpdateSessionStore(paths, log);
        var applier = new SelfUpdateApplier(sessionStore, log);

        var exitCode = applier.RunAsync(sessionId).GetAwaiter().GetResult();
        log.Info($"Self-update helper exited with code {exitCode}");
        Environment.Exit(exitCode);
    }

    private static void RunNormalMode()
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

        var appVersionInfo = AppVersionInfo.Detect();
        logger.Info($"Application started. version={appVersionInfo.RawVersion}");

        var gitHubHttpClient = new HttpClient();
        var gitHubClient = new GitHubUpdateClient(gitHubHttpClient, logger);
        var selectionPolicy = new UpdateSelectionPolicy(logger);

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        try
        {
            Application.Run(new MainForm(
                configStore, apiClient, gameDetector,
                stateService, compatService,
                localizationInstaller, backupStore, stateStore, logger,
                appVersionInfo, gitHubClient, selectionPolicy, appPaths));
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
