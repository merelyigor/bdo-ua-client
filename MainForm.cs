using System.Diagnostics;
using System.Windows.Forms;
using BdoClient.Api;
using BdoClient.Logging;
using BdoClient.Models;
using BdoClient.Services;
using BdoClient.Storage;
using BdoClient.Update;

namespace BdoClient;

public partial class MainForm : Form
{
    private readonly ConfigStore _configStore;
    private readonly ApplicationConfigStore _applicationConfigStore;
    private readonly BdoUaApiClient _apiClient;
    private readonly GameDetector _gameDetector;
    private readonly BdoGameDefinition _gameDefinition;
    private readonly GameCatalog _gameCatalog;
    private GameDescriptor _selectedGame;
    private readonly LocalizationStateService _stateService;
    private readonly LocalizationCompatibilityService _compatService;
    private readonly LocalizationInstaller _localizationInstaller;
    private readonly BackupStore _backupStore;
    private readonly InstallationStateStore _stateStore;
    private readonly ILogger _logger;
    private readonly ReleaseFeedPoller _poller;
    private readonly FeedApplicationCoordinator _feedCoordinator;
    private readonly LocalizationNotificationTracker _localizationNotificationTracker = new();
    private readonly ApplicationUpdateNotificationTracker _applicationUpdateNotificationTracker = new();
    private readonly AppVersionInfo _appVersionInfo;
    private readonly GitHubUpdateClient _gitHubClient;
    private readonly UpdateSelectionPolicy _selectionPolicy;
    private readonly AppPaths _appPaths;
    private readonly UpdatePackageService _updatePackageService;
    private readonly UpdateSessionStore _updateSessionStore;
    private readonly SelfUpdatePreparationService _selfUpdatePreparation;
    private readonly UpdateLifecycleService _updateLifecycle;
    private readonly ReleaseFeedCacheStore _releaseFeedCacheStore;

    private const string UninstallInstructions =
        "BDO-UA Client — portable-застосунок. Він не встановлюється через Windows Installer і не має окремого деінсталятора у Windows." +
        "\n\n" +
        "Звичайне видалення:\n" +
        "1. Якщо увімкнено автозапуск, вимкніть його в меню трея «Запускати разом із Windows».\n" +
        "2. Повністю завершіть клієнт: відкрийте меню трея та виберіть «Вихід». Натискання X лише ховає клієнт у трей.\n" +
        "3. Видаліть файл BDO-UA-Client.exe." +
        "\n\n" +
        "Повне очищення даних (необов’язково): після завершення клієнта можна видалити папку %LocalAppData%\\BDO-UA-Client. У ній можуть зберігатися конфігурація, логи, стан встановлення, тимчасові cache-файли, резервні копії та дані сесій оновлення. Видаляйте цю папку лише якщо хочете втратити ці дані." +
        "\n\n" +
        "Важливо: видалення клієнта або його даних не відновлює і не видаляє локалізацію у Black Desert Online. Якщо потрібно повернути оригінальну локалізацію гри, спочатку використайте в клієнті дію «Відновити оригінал», а вже потім завершіть і видаліть клієнт.";

    private string? _gameRoot;
    private DetectionSource? _gameDetectionSource;
    private GamePatchStatus _gamePatchStatus = GamePatchStatus.Unknown;
    private bool _gamePatchRefreshFailed;
    private ReleasesResponse? _apiResponse;
    private bool _apiLoadedSuccessfully;
    private ReleaseFeedSource _releaseFeedSource = ReleaseFeedSource.Unavailable;
    private DateTimeOffset? _cachedFeedSavedAtUtc;
    private string? _apiErrorMessage;
    private ApiErrorKind _apiErrorKind;
    private bool _initializing;
    private bool _suppressModeChanged;
    private volatile bool _operationInProgress;
    private volatile bool _closing;
    private bool _exitAfterOperation;
    private LocalizationState _lastResolvedState;
    private string? _lastInstalledModeSlug;
    private string? _lastInstalledPublicId;
    private OperationState _operationState = OperationState.Idle;
    private CancellationTokenSource? _operationCts;
    private System.Windows.Forms.Timer? _startupTimer;
    private DateTime _startupStartTime;

    private CancellationTokenSource? _updateCheckCts;
    private Task? _updateCheckTask;
    private System.Windows.Forms.Timer? _applicationUpdateTimer;
    private UpdateCandidate? _pendingUpdateCandidate;
    private UpdateSession? _stagedUpdateSession;
    private volatile bool _updateHandoffInProgress;
    private bool _contentFitScheduled;
    private bool _contentFitInProgress;

    public MainForm(
        ApplicationConfigStore applicationConfigStore,
        GameCatalog gameCatalog,
        BdoGameSession gameSession,
        ILogger logger,
        AppVersionInfo appVersionInfo,
        GitHubUpdateClient gitHubClient,
        UpdateSelectionPolicy selectionPolicy,
        AppPaths appPaths,
        WindowsAutostartService autostartService,
        bool startInBackground,
        SingleInstanceCoordinator singleInstanceCoordinator)
    {
        ArgumentNullException.ThrowIfNull(gameSession);
        ArgumentNullException.ThrowIfNull(gameCatalog);
        if (!string.Equals(gameSession.Descriptor.Id, gameCatalog.DefaultGame.Id, StringComparison.Ordinal))
            throw new ArgumentException("Game catalog and session must identify the same game.", nameof(gameSession));

        _applicationConfigStore = applicationConfigStore;
        _gameCatalog = gameCatalog ?? throw new ArgumentNullException(nameof(gameCatalog));
        _selectedGame = gameSession.Descriptor;
        _configStore = gameSession.ConfigStore;
        _apiClient = gameSession.ApiClient;
        _gameDetector = gameSession.GameDetector;
        _gameDefinition = gameSession.GameDefinition;
        _stateService = gameSession.LocalizationStateService;
        _compatService = gameSession.LocalizationCompatibilityService;
        _localizationInstaller = gameSession.LocalizationInstaller;
        _backupStore = gameSession.BackupStore;
        _stateStore = gameSession.InstallationStateStore;
        _logger = logger;
        _appVersionInfo = appVersionInfo;
        _gitHubClient = gitHubClient;
        _selectionPolicy = selectionPolicy;
        _appPaths = appPaths;
        _releaseFeedCacheStore = gameSession.ReleaseFeedCacheStore;
        _autostartService = autostartService;
        _startInBackground = startInBackground;
        _singleInstanceCoordinator = singleInstanceCoordinator;

        _updateSessionStore = new UpdateSessionStore(appPaths, logger);
        var manifestValidator = new UpdateManifestValidator(logger);
        _updatePackageService = new UpdatePackageService(gitHubClient, manifestValidator, _updateSessionStore, appPaths, logger);
        _selfUpdatePreparation = new SelfUpdatePreparationService(_updateSessionStore, logger);
        _updateLifecycle = new UpdateLifecycleService(_updateSessionStore, appPaths, logger);

        _poller = gameSession.ReleaseFeedPoller;
        _feedCoordinator = new FeedApplicationCoordinator(ApplyFeedPipelineAsync, _poller, _logger);
        _poller.OnFeedCandidate += OnReleaseFeedCandidate;
        _poller.OnFeedSuccess += OnReleaseFeedSuccess;

        InitializeComponent();
        InitializeTray();
        rootScrollPanel.Resize += RootScrollPanel_Resize;
        ApplyTheme();
        InitializeGameSelector();
        WireEventHandlers();
        this.Shown += MainForm_Shown;
        HandleCreated += (_, _) =>
        {
            WindowChromeHelper.ApplyDarkCaption(this);
            RegisterSecondaryActivationListener();
        };
    }

    internal GameDescriptor SelectedGame => _selectedGame;
    internal ComboBox GameSelector => gameSelectorComboBox;

    private void InitializeGameSelector()
    {
        gameSelectorComboBox.DisplayMember = nameof(GameDescriptor.DisplayName);
        gameSelectorComboBox.ValueMember = nameof(GameDescriptor.Id);
        gameSelectorComboBox.DataSource = _gameCatalog.Games.ToList();
        gameSelectorComboBox.SelectedValue = _selectedGame.Id;
        gameSelectorComboBox.Enabled = _gameCatalog.Games.Count > 1;
    }

    private void WireEventHandlers()
    {
        detectGameButton.Click += DetectGameButton_Click;
        browseGameButton.Click += BrowseGameButton_Click;
        restoreOriginalButton.Click += RestoreOriginalButton_Click;
        cancelButton.Click += CancelButton_Click;
        updateButton.Click += UpdateButton_Click;
        logsButton.Click += LogsButton_Click;
        uninstallHelpLink.LinkClicked += UninstallHelpLink_LinkClicked;
        modesFlowPanel.Resize += ModesFlowPanel_Resize;
        this.FormClosing += MainForm_FormClosing;
    }

    // --- Dynamic modes ---

    // --- Detect button ---

    // --- Browse button ---

    // --- Mode change ---


    // --- FormClosing safety ---

    internal enum MainFormCloseAction
    {
        HideToTray,
        ExitNow,
        DeferUntilOperationCompletes
    }

    /// <summary>
    /// Pure close-policy decision used by MainForm_FormClosing. Self-update
    /// handoff is intentionally excluded and handled as a first branch outside
    /// this helper.
    /// </summary>
    internal static MainFormCloseAction EvaluateCloseAction(
        CloseReason closeReason,
        bool explicitExitRequested,
        bool exitAfterOperation,
        bool operationInProgress)
    {
        // A normal manual X (no explicit tray Exit, no pending synthetic re-close)
        // always hides to tray — even while an operation is active.
        if (closeReason == CloseReason.UserClosing
            && !explicitExitRequested
            && !exitAfterOperation)
        {
            return MainFormCloseAction.HideToTray;
        }

        if (operationInProgress)
            return MainFormCloseAction.DeferUntilOperationCompletes;

        return MainFormCloseAction.ExitNow;
    }

    private void MainForm_FormClosing(object? sender, FormClosingEventArgs e)
    {
        // Self-update handoff is a special real-exit path and must remain first.
        // Never convert self-update into hide-to-tray or generic pending-exit.
        if (_updateHandoffInProgress)
        {
            PrepareTrayForShutdown();
            _updateCheckCts?.Cancel();
            _poller.Stop();
            return;
        }

        var action = EvaluateCloseAction(
            e.CloseReason, _explicitExitRequested, _exitAfterOperation, _operationInProgress);

        switch (action)
        {
            case MainFormCloseAction.HideToTray:
                // Normal user close (X / Alt+F4) → hide to tray.
                // Must occur before active-operation cancellation logic so an ongoing
                // localization operation is NOT cancelled by simply closing the window.
                e.Cancel = true;
                HideToTray();
                ScheduleAutostartOfferAfterManualHide();
                return;

            case MainFormCloseAction.ExitNow:
                _closing = true;
                _exitAfterOperation = false;
                PrepareTrayForShutdown();
                _updateCheckCts?.Cancel();
                _poller.Stop();
                return;

            case MainFormCloseAction.DeferUntilOperationCompletes:
            default:
                // Real close requested while an operation is active: defer termination
                // to protect game-file integrity, request cancellation, and let the
                // operation reach its existing safe cleanup boundary before exiting.
                // This also covers Windows/system close reasons (deferred, not hidden).
                e.Cancel = true;
                _closing = true;
                _exitAfterOperation = true;
                RequestOperationCancelForShutdown();
                return;
        }
    }

    private void RequestOperationCancelForShutdown()
    {
        if (_operationCts != null && !_operationCts.IsCancellationRequested)
        {
            cancelButton.Enabled = false;
            SetMessage("Скасування операції перед закриттям...");
            _operationCts.Cancel();
        }
        else
        {
            SetMessage("Дочекайтеся завершення поточної операції.");
        }
    }

    /// <summary>
    /// Called at the safe completion boundary of an operation. If a real exit was
    /// deferred because the operation was active, schedules the final Close on the
    /// UI thread. The resulting UserClosing re-enters MainForm_FormClosing where
    /// operationInProgress is false and _exitAfterOperation is true → ExitNow.
    /// </summary>
    private void CompletePendingExitAfterOperation()
    {
        if (_exitAfterOperation == false)
            return;
        if (_updateHandoffInProgress)
            return;
        if (IsDisposed || Disposing || !IsHandleCreated)
            return;

        BeginInvoke(new Action(Close));
    }


    // --- Logs button ---

    private void UninstallHelpLink_LinkClicked(object? sender, LinkLabelLinkClickedEventArgs e)
    {
        MessageBox.Show(
            this,
            UninstallInstructions,
            "Як видалити BDO-UA Client?",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private void LogsButton_Click(object? sender, EventArgs e)
    {
        try
        {
            var logsDir = _appPaths.LogsDir;
            Directory.CreateDirectory(logsDir);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = logsDir,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to open logs folder: {ex.Message}");
            MessageBox.Show(
                "Не вдалося відкрити папку журналів.",
                "Помилка",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    // --- Release feed polling ---

    /// <summary>
    /// Whole-pipeline feed application callback used by FeedApplicationCoordinator.
    /// Returns true only if all stages succeed (API update, mode rebuild, selection, state refresh).
    /// </summary>

    // --- Game status presentation ---

    // --- Operation state ---

    // --- State refresh ---


}
