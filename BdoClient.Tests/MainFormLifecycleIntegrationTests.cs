using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Windows.Forms;
using BdoClient.Api;
using BdoClient.Logging;
using BdoClient.Models;
using BdoClient.Services;
using BdoClient.Storage;
using BdoClient.Update;

namespace BdoClient.Tests;

public sealed class MainFormLifecycleIntegrationTests
{
    [Fact]
    public async Task Startup_ComposesMainFormAndCompletesWithSavedGame()
    {
        using var fixture = await MainFormTestFixture.StartAsync(
            MainFormTestFixture.CreateSuccessfulApiHandler());

        var startup = await fixture.WaitForStartupAsync();

        Assert.True(startup.Form.IsHandleCreated);
        Assert.False(startup.Form.IsDisposed);
        Assert.Equal("✓ Гру знайдено", startup.GameStatus);
        Assert.Equal(fixture.GameRoot, startup.GamePath);
        Assert.True(startup.ApiRequestCount >= 1);
        Assert.True(startup.GitHubRequestCount >= 1);
    }

    [Fact]
    public async Task Startup_ApiFailureLeavesFormCoherentAndTerminatesCleanly()
    {
        using var fixture = await MainFormTestFixture.StartAsync(
            MainFormTestFixture.CreateFailureApiHandler(HttpStatusCode.ServiceUnavailable));

        var startup = await fixture.WaitForStartupAsync();

        Assert.True(startup.Form.IsHandleCreated);
        Assert.False(startup.Form.IsDisposed);
        Assert.Equal("✓ Гру знайдено", startup.GameStatus);
        Assert.Equal(fixture.GameRoot, startup.GamePath);
        Assert.Equal("Сервер повернув помилку.", startup.Message);
        Assert.True(startup.GitHubRequestCount >= 1);
    }

    [Fact]
    public async Task Startup_ApiFailureWithValidCache_ShowsCachedFeedAndDisablesWrites()
    {
        var handler = MainFormTestFixture.CreateFailureApiHandler(HttpStatusCode.ServiceUnavailable);
        using var fixture = await MainFormTestFixture.StartAsync(handler, seedReleaseFeedCache: true);

        var startup = await fixture.WaitForStartupAsync();
        var card = MainFormTestFixture.FindFirstModeCard(startup.Form);

        Assert.True(startup.Form.IsHandleCreated);
        Assert.Contains("Сервер недоступний.", startup.Message);
        Assert.Contains("збережені дані", startup.Message);
        Assert.NotNull(card);
        Assert.False(card!.Controls.OfType<Button>().Single().Enabled);
    }

    [Fact]
    public async Task Startup_OutdatedGame_ShowsWarningWithInstalledAndLatestPatches()
    {
        using var fixture = await MainFormTestFixture.StartAsync(
            MainFormTestFixture.CreateSuccessfulApiHandler(401),
            gamePatch: 399);

        string? status = null;
        await fixture.WaitForAsync(form =>
        {
            status = MainFormTestFixture.FindControlText(
                form, text => text.StartsWith("⚠ Потрібно оновити гру", StringComparison.Ordinal));
            return status != null;
        });

        Assert.Equal(
            $"⚠ Потрібно оновити гру{Environment.NewLine}Встановлено: patch 399 • актуальний: patch 401",
            status);
    }

    [Fact]
    public async Task SecondaryActivationRestoresExistingBackgroundForm()
    {
        using var fixture = await MainFormTestFixture.StartAsync(
            MainFormTestFixture.CreateSuccessfulApiHandler(),
            startInBackground: true);

        await fixture.WaitForStartupAsync();
        await fixture.WaitForAsync(form => !form.Visible && form.WindowState == FormWindowState.Minimized);

        var originalForm = fixture.Form;
        fixture.SignalSecondaryActivation();

        await fixture.WaitForAsync(form => form.Visible && form.WindowState == FormWindowState.Normal);

        Assert.Same(originalForm, fixture.Form);
    }

    [Fact]
    public async Task ClosingWhileStartupRequestIsPendingStopsTestHostWithoutOrphanUiThread()
    {
        var handler = MainFormTestFixture.CreatePendingApiHandler();
        using var fixture = await MainFormTestFixture.StartAsync(
            handler,
            exitWhenShown: true);

        await handler.RequestStarted.WaitAsync(MainFormTestFixture.Timeout);
        await fixture.WaitForHostExitAsync();

        Assert.Null(fixture.HostException);
        Assert.False(fixture.IsHostAlive);
    }
}

internal sealed class MainFormTestFixture : IDisposable
{
    internal static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    private readonly string _root;
    private readonly AppPaths _appPaths;
    private readonly SingleInstanceCoordinator _singleInstanceCoordinator;
    private readonly HttpClient _bdoHttpClient;
    private readonly HttpClient _githubHttpClient;
    private readonly MainFormTestHttpHandler _bdoHandler;
    private readonly MainFormTestHttpHandler _githubHandler;
    private readonly TaskCompletionSource<MainForm> _formReady =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<object?> _hostCompleted =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly Thread _uiThread;
    private Exception? _hostException;
    private MainForm? _form;
    private bool _disposed;

    private MainFormTestFixture(
        MainFormTestHttpHandler bdoHandler,
        bool startInBackground,
        bool exitWhenShown,
        bool seedReleaseFeedCache,
        int? gamePatch)
    {
        _bdoHandler = bdoHandler;
        _githubHandler = CreateSuccessfulGitHubHandler();
        _root = Path.Combine(Path.GetTempPath(), "bdo-ua-mainform-tests", Guid.NewGuid().ToString("N"));
        _appPaths = new AppPaths(Path.Combine(_root, "appdata"));
        _appPaths.EnsureDirectories();

        if (seedReleaseFeedCache)
        {
            var cacheStore = new ReleaseFeedCacheStore(_appPaths, new TestLogger());
            cacheStore.SaveAsync(CreateCachedFeed(),
                new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero))
                .GetAwaiter().GetResult();
        }

        GameRoot = Path.Combine(_root, "fake-game");
        Directory.CreateDirectory(Path.Combine(GameRoot, "ads"));
        File.WriteAllBytes(GamePaths.GetLocalizationFilePath(GameRoot), Array.Empty<byte>());
        if (gamePatch is > 0)
            File.WriteAllText(Path.Combine(GameRoot, "ads_files"), $"languagedata_en.loc\t{gamePatch.Value}\n");
        File.WriteAllText(
            _appPaths.ConfigFile,
            JsonSerializer.Serialize(new Config { GamePath = GameRoot }));

        _bdoHttpClient = new HttpClient(_bdoHandler);
        _githubHttpClient = new HttpClient(_githubHandler);

        var mutexName = $@"Local\BdoClient.Tests.{Guid.NewGuid():N}.Mutex";
        var eventName = $@"Local\BdoClient.Tests.{Guid.NewGuid():N}.Activate";
        _singleInstanceCoordinator = new SingleInstanceCoordinator(mutexName, eventName);

        _uiThread = new Thread(() => RunUiLoop(startInBackground, exitWhenShown))
        {
            IsBackground = false,
            Name = "BdoClient.MainFormLifecycleTest"
        };
        _uiThread.SetApartmentState(ApartmentState.STA);
    }

    internal string GameRoot { get; }

    internal MainForm Form => _form ?? throw new InvalidOperationException("MainForm is not ready.");

    internal Exception? HostException => _hostException;

    internal bool IsHostAlive => _uiThread.IsAlive;

    internal static async Task<MainFormTestFixture> StartAsync(
        MainFormTestHttpHandler bdoHandler,
        bool startInBackground = false,
        bool exitWhenShown = false,
        bool seedReleaseFeedCache = false,
        int? gamePatch = null)
    {
        var fixture = new MainFormTestFixture(
            bdoHandler, startInBackground, exitWhenShown, seedReleaseFeedCache, gamePatch);
        fixture._uiThread.Start();

        try
        {
            await fixture._formReady.Task.WaitAsync(Timeout);
            return fixture;
        }
        catch
        {
            fixture.Dispose();
            throw;
        }
    }

    internal static MainFormTestHttpHandler CreateSuccessfulApiHandler(int officialPatch = 0)
        => new(HttpStatusCode.OK, $"{{\"success\":true,\"data\":{{\"official_patch\":{officialPatch},\"modes\":[]}}}}");

    internal static MainFormTestHttpHandler CreateSuccessfulGitHubHandler()
        => new(HttpStatusCode.OK, "[]");

    internal static MainFormTestHttpHandler CreateFailureApiHandler(HttpStatusCode statusCode)
        => new(statusCode, string.Empty);

    internal static MainFormTestHttpHandler CreatePendingApiHandler()
        => new(HttpStatusCode.OK, "{\"success\":true,\"data\":{\"modes\":[]}}", waitForRelease: true);

    private static ReleasesResponse CreateCachedFeed() => new()
    {
        Success = true,
        Data = new ReleaseData
        {
            OfficialPatch = 100,
            Modes = new List<LocalizationMode>
            {
                new()
                {
                    Slug = "full-ukrainian",
                    PublicName = "Повна українська",
                    Description = "Cached mode",
                    Current = new CurrentRelease
                    {
                        PublicId = "01CACHED",
                        Version = 1,
                        Patch = 100,
                        DownloadUrl = "https://example.com/cached.loc",
                        SizeBytes = 1024,
                        Sha256 = new string('b', 64),
                        CompatibleWithOfficialPatch = true
                    }
                }
            }
        }
    };

    internal async Task<StartupSnapshot> WaitForStartupAsync()
    {
        await _githubHandler.RequestStarted.WaitAsync(Timeout);

        var snapshot = await WaitForAsync(form =>
        {
            var gameStatus = FindControlText(form, text => text == "✓ Гру знайдено");
            var gamePath = FindControlText(form, text => text == GameRoot);
            var degraded = FindControlText(form, text => text == "Сервер повернув помилку.");
            var cached = FindControlText(form, text => text.StartsWith("Сервер недоступний.", StringComparison.Ordinal));

            var success = gameStatus != null && gamePath != null;
            var failure = success && _bdoHandler.StatusCode != HttpStatusCode.OK
                && (degraded != null || cached != null);
            return success && (_bdoHandler.StatusCode == HttpStatusCode.OK || failure)
                ? new StartupSnapshot(
                    form,
                    gameStatus,
                    gamePath,
                    cached ?? degraded,
                    _bdoHandler.RequestCount,
                    _githubHandler.RequestCount)
                : null;
        });

        return snapshot!;
    }

    internal async Task WaitForAsync(Func<MainForm, bool> predicate)
    {
        var completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

        void Check()
        {
            try
            {
                if (_form == null || _form.IsDisposed || _form.Disposing)
                {
                    completion.TrySetException(new InvalidOperationException("MainForm closed before condition was met."));
                    return;
                }

                if (predicate(_form))
                {
                    completion.TrySetResult(null);
                    return;
                }

                _form.BeginInvoke((Action)Check);
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        }

        PostToUi(Check);
        await completion.Task.WaitAsync(Timeout);
    }

    internal void SignalSecondaryActivation()
    {
        _singleInstanceCoordinator.SignalActivation();
    }

    internal async Task WaitForHostExitAsync()
    {
        await _hostCompleted.Task.WaitAsync(Timeout);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        _bdoHandler.ReleasePendingRequest();

        if (_uiThread.IsAlive)
        {
            try
            {
                PostToUi(Application.ExitThread);
                if (!_uiThread.Join(Timeout))
                    throw new TimeoutException("MainForm test UI thread did not terminate.");
            }
            catch
            {
                if (_uiThread.IsAlive)
                    _uiThread.Interrupt();
                throw;
            }
        }

        _singleInstanceCoordinator.Dispose();
        _bdoHttpClient.Dispose();
        _githubHttpClient.Dispose();

        if (_hostException != null)
            throw new InvalidOperationException("MainForm test UI thread failed.", _hostException);

        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private void RunUiLoop(bool startInBackground, bool exitWhenShown)
    {
        try
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            var logger = new TestLogger();
            var configStore = new ConfigStore(_appPaths, logger);
            var stateStore = new InstallationStateStore(_appPaths, logger);
            var apiClient = new BdoUaApiClient(_bdoHttpClient, logger);
            var localizationInstaller = new LocalizationInstaller(_bdoHttpClient, _appPaths, logger);
            var backupStore = new BackupStore(_appPaths, logger);
            var gameDetector = new GameDetector(configStore, logger);
            var stateService = new LocalizationStateService(stateStore, logger);
            var compatService = new LocalizationCompatibilityService();
            var appVersionInfo = AppVersionInfo.FromRawVersion("1.2.2");
            var githubClient = new GitHubUpdateClient(_githubHttpClient, logger);
            var selectionPolicy = new UpdateSelectionPolicy(logger);
            var autostartService = new WindowsAutostartService(
                Path.Combine(_root, "BDO-UA-Client.exe"), logger);

            _form = new MainForm(
                configStore,
                apiClient,
                gameDetector,
                stateService,
                compatService,
                localizationInstaller,
                backupStore,
                stateStore,
                logger,
                appVersionInfo,
                githubClient,
                selectionPolicy,
                _appPaths,
                autostartService,
                startInBackground,
                _singleInstanceCoordinator);

            _form.HandleCreated += (_, _) => _formReady.TrySetResult(_form);
            if (exitWhenShown)
            {
                _form.Shown += (_, _) => _form.BeginInvoke(new Action(Application.ExitThread));
            }

            Application.Run(_form);
        }
        catch (Exception ex)
        {
            _hostException = ex;
            _formReady.TrySetException(ex);
        }
        finally
        {
            try
            {
                if (_form != null && !_form.IsDisposed)
                    _form.Dispose();
            }
            catch (Exception ex)
            {
                _hostException ??= ex;
            }

            _hostCompleted.TrySetResult(null);
        }
    }

    private async Task<T> WaitForAsync<T>(Func<MainForm, T?> read)
        where T : class
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

        void Check()
        {
            try
            {
                if (_form == null || _form.IsDisposed || _form.Disposing)
                {
                    completion.TrySetException(new InvalidOperationException("MainForm closed before condition was met."));
                    return;
                }

                var result = read(_form);
                if (result != null)
                {
                    completion.TrySetResult(result);
                    return;
                }

                _form.BeginInvoke((Action)Check);
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        }

        PostToUi(Check);
        return await completion.Task.WaitAsync(Timeout);
    }

    private void PostToUi(Action action)
    {
        var form = _formReady.Task.WaitAsync(Timeout).GetAwaiter().GetResult();
        form.BeginInvoke(action);
    }

    internal static string? FindControlText(Control root, Func<string, bool> predicate)
    {
        foreach (Control child in root.Controls)
        {
            if (predicate(child.Text))
                return child.Text;

            var nested = FindControlText(child, predicate);
            if (nested != null)
                return nested;
        }

        return null;
    }

    internal static LocalizationModeCard? FindFirstModeCard(Control root)
    {
        foreach (Control child in root.Controls)
        {
            if (child is LocalizationModeCard card)
                return card;

            var nested = FindFirstModeCard(child);
            if (nested != null)
                return nested;
        }

        return null;
    }

    internal sealed record StartupSnapshot(
        MainForm Form,
        string? GameStatus,
        string? GamePath,
        string? Message,
        int ApiRequestCount,
        int GitHubRequestCount);

    internal sealed class TestLogger : ILogger
    {
        public void Debug(string message) { }
        public void Info(string message) { }
        public void Warning(string message) { }
        public void Error(string message) { }
    }
}

internal sealed class MainFormTestHttpHandler : HttpMessageHandler
{
    private readonly HttpStatusCode _statusCode;
    private readonly string _response;
    private readonly bool _waitForRelease;
    private readonly TaskCompletionSource<object?> _requestStarted =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<object?> _release =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _requestCount;

    internal MainFormTestHttpHandler(
        HttpStatusCode statusCode,
        string response,
        bool waitForRelease = false)
    {
        _statusCode = statusCode;
        _response = response;
        _waitForRelease = waitForRelease;
    }

    internal HttpStatusCode StatusCode => _statusCode;

    internal int RequestCount => Volatile.Read(ref _requestCount);

    internal Task RequestStarted => _requestStarted.Task;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (request.Method == HttpMethod.Get && request.RequestUri?.AbsolutePath.EndsWith("/releases", StringComparison.Ordinal) == true)
        {
            Interlocked.Increment(ref _requestCount);
            _requestStarted.TrySetResult(null);

            if (_waitForRelease)
                await _release.Task.WaitAsync(cancellationToken);
        }

        return new HttpResponseMessage(_statusCode)
        {
            Content = new StringContent(_response, Encoding.UTF8, "application/json")
        };
    }

    internal void ReleasePendingRequest()
    {
        _release.TrySetResult(null);
    }
}
