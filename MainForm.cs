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

    // --- Background update check ---

    private void StartBackgroundUpdateCheck()
    {
        versionLabel.Text = _appVersionInfo.DisplayVersion;

        if (!_appVersionInfo.IsPublicRelease)
        {
            _logger.Debug($"Update check skipped: not a public release ({_appVersionInfo.RawVersion})");
            return;
        }

        _updateCheckCts = new CancellationTokenSource();
        _updateCheckTask = RunUpdateCheckAsync(_updateCheckCts.Token);
    }

    private async Task RunUpdateCheckAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.Debug("Update check started");
            var result = await _gitHubClient.FetchReleasesAsync(cancellationToken);

            if (cancellationToken.IsCancellationRequested || _closing) return;

            if (!result.IsSuccess)
            {
                _logger.Warning($"Update check failed: {result.ErrorMessage}");
                return;
            }

            var candidate = _selectionPolicy.FindUpdate(_appVersionInfo, result.Value!);

            if (cancellationToken.IsCancellationRequested || _closing) return;

            if (candidate != null)
            {
                _pendingUpdateCandidate = candidate;
                _logger.Info($"Update available: {candidate.TagName}");
                RefreshUpdateButtonPresentation();
            }
            else
            {
                _logger.Debug("Update check: no eligible update");
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
        }
        catch (Exception ex)
        {
            _logger.Error($"Update check error: {ex.Message}");
        }
    }

    private void RefreshUpdateButtonPresentation()
    {
        if (_stagedUpdateSession != null)
        {
            updateButton.Visible = false;
            updateButton.Enabled = false;
            return;
        }

        var state = UpdateButtonState.Compute(_pendingUpdateCandidate, _operationInProgress);
        updateButton.Text = state.Text;
        updateButton.Visible = state.Visible;
        updateButton.Enabled = state.Enabled;
        UiTheme.RefreshButtonState(updateButton);
    }

    // --- Update button ---

    private async void UpdateButton_Click(object? sender, EventArgs e)
    {
        try
        {
            await HandleApplicationUpdateDownloadAsync();
        }
        catch (Exception ex)
        {
            _logger.Error($"UpdateButton_Click unexpected: {ex.Message}");
            SetMessage("Не вдалося виконати оновлення. Відкрийте папку журналів для деталей.");
        }
    }

    private async Task HandleApplicationUpdateDownloadAsync()
    {
        if (_operationInProgress) return;
        if (_pendingUpdateCandidate == null) return;

        string? finalMessage = null;

        try
        {
            _operationInProgress = true;
            _poller.Pause();
            _feedCoordinator.BlockUpdates();
            SetOperationState(OperationState.Idle);
            SetActionsEnabled(false);
            SetControlsDuringOperation(false);

            SetMessage($"Завантаження оновлення {_pendingUpdateCandidate.TagName}...");
            SetProgress(0);
            SetOperationState(OperationState.Downloading);

            _operationCts = new CancellationTokenSource();
            cancelButton.Visible = true;
            cancelButton.Enabled = true;
            UpdateCancelButtonVisibility(_operationState);

            var progress = new Progress<UpdateStageProgress>(stage =>
            {
                SetMessage(stage.Message);
                if (stage.Percent > 0)
                {
                    var clamped = (int)Math.Clamp(Math.Round(stage.Percent), 0, 100);
                    progressBar.Style = ProgressBarStyle.Continuous;
                    progressBar.Value = clamped;
                    progressLabel.Text = $"{clamped}%";
                }
            });

            var result = await _updatePackageService.StageUpdateAsync(
                _pendingUpdateCandidate, _appVersionInfo, progress, _operationCts.Token);

            if (result.IsSuccess)
            {
                SetOperationState(OperationState.Completed);
                _stagedUpdateSession = result.Session;

                // Prepare: copy candidate, capture original SHA, mark prepared
                SetMessage("Підготовка оновлення...");
                var prepResult = await _selfUpdatePreparation.PrepareAsync(result.Session!.SessionId, _operationCts.Token);

                if (!prepResult.IsSuccess)
                {
                    _logger.Error($"Self-update preparation failed: {prepResult.Error} — {prepResult.ErrorMessage}");
                    finalMessage = MapPreparationError(prepResult.Error!.Value);
                    _updateSessionStore.CleanupSession(result.Session!.SessionId);
                    _stagedUpdateSession = null;
                    return;
                }

                // Derive staged helper path from session store
                var stagedHelperPath = Path.Combine(
                    _updateSessionStore.GetSessionDir(result.Session!.SessionId),
                    "BDO-UA-Client.exe");

                if (!File.Exists(stagedHelperPath))
                {
                    _logger.Error($"Self-update handoff: staged helper not found at {stagedHelperPath}");
                    finalMessage = "Не вдалося знайти підготовлений файл оновлення.";
                    _updateSessionStore.CleanupSession(result.Session!.SessionId);
                    _stagedUpdateSession = null;
                    return;
                }

                // Disable cancel before handoff boundary
                cancelButton.Visible = false;
                cancelButton.Enabled = false;

                // Launch helper
                SetMessage("Підготовка оновлення... Програма буде перезапущена.");
                Process? helperProcess = null;
                try
                {
                    var stagedDir = Path.GetDirectoryName(stagedHelperPath)!;
                    var psi = new ProcessStartInfo
                    {
                        FileName = stagedHelperPath,
                        UseShellExecute = false,
                        WorkingDirectory = stagedDir
                    };
                    psi.ArgumentList.Add("--apply-update");
                    psi.ArgumentList.Add(result.Session!.SessionId);
                    helperProcess = Process.Start(psi);
                    _logger.Info($"Self-update: launched helper at {stagedHelperPath}");
                }
                catch (Exception ex)
                {
                    _logger.Error($"Self-update handoff: failed to launch helper: {ex.Message}");
                    finalMessage = "Не вдалося запустити процес оновлення.";
                    RestorePostHandoffFailureState();
                    return;
                }

                if (helperProcess == null)
                {
                    _logger.Error("Self-update handoff: Process.Start returned null");
                    finalMessage = "Не вдалося запустити процес оновлення.";
                    RestorePostHandoffFailureState();
                    return;
                }

                // Handoff successful — set flag and exit
                _updateHandoffInProgress = true;
                _poller.Stop();
                _updateCheckCts?.Cancel();
                _logger.Info("Self-update: exiting old process");
                Application.Exit();
            }
            else
            {
                if (result.Error == UpdatePackageError.Cancelled)
                    SetOperationState(OperationState.Cancelled);
                else
                    SetOperationState(OperationState.Failed);

                _logger.Error($"Update staging failed: {result.Error} — {result.ErrorMessage}");
                finalMessage = MapUpdatePackageError(result.Error!.Value);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.Info("Update staging cancelled by user.");
            CleanupAbandonedStagingSession();
            SetOperationState(OperationState.Cancelled);
            finalMessage = "Оновлення скасовано.";
        }
        catch (Exception ex)
        {
            _logger.Error($"Update staging error: {ex.Message}");
            CleanupAbandonedStagingSession();
            SetOperationState(OperationState.Failed);
            finalMessage = "Не вдалося виконати оновлення. Відкрийте папку журналів для деталей.";
        }
        finally
        {
            cancelButton.Visible = false;
            cancelButton.Enabled = false;
            _operationCts?.Dispose();
            _operationCts = null;
            _operationInProgress = false;

            if (!_updateHandoffInProgress)
            {
                SetControlsDuringOperation(true);

                try
                {
                    if (finalMessage != null)
                        SetMessage(finalMessage);

                    await _feedCoordinator.ApplyPendingIfAnyAsync();
                }
                finally
                {
                    _feedCoordinator.UnblockUpdates();
                    if (!_closing)
                        _poller.Resume();
                }
            }
        }
    }

    private static string MapUpdatePackageError(UpdatePackageError error) => error switch
    {
        UpdatePackageError.InvalidCandidate => "Неприйнятний кандидат оновлення.",
        UpdatePackageError.ManifestDownloadFailed => "Не вдалося отримати метадані оновлення. Спробуйте ще раз пізніше.",
        UpdatePackageError.ManifestInvalid => "Оновлення не пройшло перевірку цілісності. Поточна версія не змінена.",
        UpdatePackageError.AssetMissing => "Не знайдено файл оновлення.",
        UpdatePackageError.DownloadFailed => "Не вдалося завантажити оновлення. Спробуйте ще раз пізніше.",
        UpdatePackageError.SizeMismatch => "Оновлення не пройшло перевірку цілісності. Поточна версія не змінена.",
        UpdatePackageError.HashMismatch => "Оновлення не пройшло перевірку цілісності. Поточна версія не змінена.",
        UpdatePackageError.PackageInvalid => "Оновлення не пройшло перевірку цілісності. Поточна версія не змінена.",
        UpdatePackageError.ExecutableInvalid => "Оновлення не пройшло перевірку цілісності. Поточна версія не змінена.",
        UpdatePackageError.SessionWriteFailed => "Не вдалося зберегти стан оновлення.",
        UpdatePackageError.IoError => "Помилка введення-виведення під час оновлення.",
        UpdatePackageError.Cancelled => "Оновлення скасовано.",
        _ => "Невідома помилка оновлення."
    };

    private static string MapPreparationError(SelfUpdatePreparationError error) => error switch
    {
        SelfUpdatePreparationError.WriteDenied =>
            "Не вдалося підготувати автоматичне оновлення, оскільки папка програми недоступна для запису.\nОновіть програму вручну або перемістіть її до папки, доступної для запису.",
        SelfUpdatePreparationError.CandidateCollision =>
            "Не вдалося підготувати оновлення: файл оновлення вже існує.\nСпробуйте ще раз або перезапустіть програму.",
        SelfUpdatePreparationError.BackupCollision =>
            "Не вдалося підготувати оновлення: резервна копія вже існує.\nСпробуйте ще раз або перезапустіть програму.",
        SelfUpdatePreparationError.CandidateCopyFailed =>
            "Не вдалося підготувати оновлення: помилка запису файлу.",
        SelfUpdatePreparationError.SessionWriteFailed =>
            "Не вдалося зберегти стан підготовки оновлення.",
        SelfUpdatePreparationError.HashMismatch =>
            "Оновлення не пройшло перевірку цілісності. Поточна версія не змінена.",
        SelfUpdatePreparationError.VersionMismatch =>
            "Оновлення не пройшло перевірку версії. Поточна версія не змінена.",
        SelfUpdatePreparationError.StagedExeMissing =>
            "Не вдалося знайти підготовлений файл оновлення.",
        SelfUpdatePreparationError.TargetMissing =>
            "Поточний виконуваний файл не знайдено. Оновлення неможливе.",
        SelfUpdatePreparationError.TargetInvalid =>
            "Шлях до програми недійсний. Оновлення неможливе.",
        SelfUpdatePreparationError.SessionInvalid =>
            "Стан оновлення недійсний. Спробуйте ще раз.",
        _ => "Не вдалося підготувати оновлення."
    };

    private void RestorePostHandoffFailureState()
    {
        _logger.Info("Self-update: restoring state after pre-handoff failure");

        if (_stagedUpdateSession != null)
        {
            var session = _stagedUpdateSession;
            try
            {
                if (TryCleanupPreparedAttempt(session))
                {
                    _updateSessionStore.CleanupSession(session.SessionId);
                    _logger.Debug($"Self-update: cleaned up session {session.SessionId}");
                }
                else
                    _logger.Warning($"Self-update: retained session {session.SessionId} because candidate identity was not verified");
            }
            catch (Exception ex)
            {
                _logger.Warning($"Self-update: pre-handoff cleanup failed (best-effort): {ex.Message}");
            }
        }

        _operationInProgress = false;
        _updateHandoffInProgress = false;
        _stagedUpdateSession = null;
        SetOperationState(OperationState.Idle);
        SetControlsDuringOperation(true);
        _feedCoordinator.UnblockUpdates();
        if (!_closing)
            _poller.Resume();
        RefreshUpdateButtonPresentation();
    }

    private bool TryCleanupPreparedAttempt(UpdateSession session)
    {
        return PreparedAttemptCleanup.TryDeleteCandidate(session, _appPaths, _logger);
    }

    private void CleanupAbandonedStagingSession()
    {
        if (_stagedUpdateSession == null)
            return;

        _updateSessionStore.CleanupSession(_stagedUpdateSession.SessionId);
        _stagedUpdateSession = null;
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
