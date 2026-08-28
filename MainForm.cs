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
    private readonly BdoUaApiClient _apiClient;
    private readonly GameDetector _gameDetector;
    private readonly LocalizationStateService _stateService;
    private readonly LocalizationCompatibilityService _compatService;
    private readonly LocalizationInstaller _localizationInstaller;
    private readonly BackupStore _backupStore;
    private readonly InstallationStateStore _stateStore;
    private readonly ILogger _logger;
    private readonly ReleaseFeedPoller _poller;
    private readonly FeedApplicationCoordinator _feedCoordinator;
    private readonly AppVersionInfo _appVersionInfo;
    private readonly GitHubUpdateClient _gitHubClient;
    private readonly UpdateSelectionPolicy _selectionPolicy;
    private readonly AppPaths _appPaths;
    private readonly UpdatePackageService _updatePackageService;
    private readonly UpdateSessionStore _updateSessionStore;
    private readonly SelfUpdatePreparationService _selfUpdatePreparation;
    private readonly UpdateLifecycleService _updateLifecycle;

    private string? _gameRoot;
    private ReleasesResponse? _apiResponse;
    private bool _apiLoadedSuccessfully;
    private string? _apiErrorMessage;
    private ApiErrorKind _apiErrorKind;
    private bool _initializing;
    private bool _suppressModeChanged;
    private volatile bool _operationInProgress;
    private volatile bool _closing;
    private LocalizationState _lastResolvedState;
    private string? _lastInstalledModeSlug;
    private string? _lastInstalledPublicId;
    private OperationState _operationState = OperationState.Idle;
    private CancellationTokenSource? _operationCts;
    private System.Windows.Forms.Timer? _startupTimer;
    private DateTime _startupStartTime;

    private CancellationTokenSource? _updateCheckCts;
    private Task? _updateCheckTask;
    private UpdateCandidate? _pendingUpdateCandidate;
    private UpdateSession? _stagedUpdateSession;
    private volatile bool _updateHandoffInProgress;
    private bool _contentFitScheduled;
    private bool _contentFitInProgress;

    public MainForm(
        ConfigStore configStore,
        BdoUaApiClient apiClient,
        GameDetector gameDetector,
        LocalizationStateService stateService,
        LocalizationCompatibilityService compatService,
        LocalizationInstaller localizationInstaller,
        BackupStore backupStore,
        InstallationStateStore stateStore,
        ILogger logger,
        AppVersionInfo appVersionInfo,
        GitHubUpdateClient gitHubClient,
        UpdateSelectionPolicy selectionPolicy,
        AppPaths appPaths)
    {
        _configStore = configStore;
        _apiClient = apiClient;
        _gameDetector = gameDetector;
        _stateService = stateService;
        _compatService = compatService;
        _localizationInstaller = localizationInstaller;
        _backupStore = backupStore;
        _stateStore = stateStore;
        _logger = logger;
        _appVersionInfo = appVersionInfo;
        _gitHubClient = gitHubClient;
        _selectionPolicy = selectionPolicy;
        _appPaths = appPaths;

        _updateSessionStore = new UpdateSessionStore(appPaths, logger);
        var manifestValidator = new UpdateManifestValidator(logger);
        _updatePackageService = new UpdatePackageService(gitHubClient, manifestValidator, _updateSessionStore, appPaths, logger);
        _selfUpdatePreparation = new SelfUpdatePreparationService(_updateSessionStore, logger);
        _updateLifecycle = new UpdateLifecycleService(_updateSessionStore, appPaths, logger);

        _poller = new ReleaseFeedPoller(_apiClient, _logger);
        _feedCoordinator = new FeedApplicationCoordinator(ApplyFeedPipelineAsync, _poller, _logger);
        _poller.OnFeedCandidate += OnReleaseFeedCandidate;

        InitializeComponent();
        rootScrollPanel.Resize += RootScrollPanel_Resize;
        ApplyTheme();
        WireEventHandlers();
        this.Shown += MainForm_Shown;
        HandleCreated += (_, _) => WindowChromeHelper.ApplyDarkCaption(this);
    }

    private void WireEventHandlers()
    {
        detectGameButton.Click += DetectGameButton_Click;
        browseGameButton.Click += BrowseGameButton_Click;
        restoreOriginalButton.Click += RestoreOriginalButton_Click;
        cancelButton.Click += CancelButton_Click;
        updateButton.Click += UpdateButton_Click;
        logsButton.Click += LogsButton_Click;
        modesFlowPanel.Resize += ModesFlowPanel_Resize;
        this.FormClosing += MainForm_FormClosing;
    }

    // --- Dynamic modes ---

    // --- Detect button ---

    // --- Browse button ---

    // --- Mode change ---


    // --- FormClosing safety ---

    private void MainForm_FormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_updateHandoffInProgress)
        {
            // Updater handoff in progress — allow close, stop background tasks
            _updateCheckCts?.Cancel();
            _poller.Stop();
            return;
        }

        if (!_operationInProgress)
        {
            _closing = true;
            _updateCheckCts?.Cancel();
            _poller.Stop();
            return;
        }

        e.Cancel = true;

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


    // --- Logs button ---

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
