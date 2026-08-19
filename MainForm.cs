using System.Windows.Forms;
using BdoClient.Api;
using BdoClient.Logging;
using BdoClient.Models;
using BdoClient.Services;
using BdoClient.Storage;

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
    private OperationState _operationState = OperationState.Idle;
    private CancellationTokenSource? _operationCts;
    private System.Windows.Forms.Timer? _startupTimer;
    private DateTime _startupStartTime;

    private static readonly Color SuccessGreen = Color.FromArgb(0, 128, 0);

    public MainForm(
        ConfigStore configStore,
        BdoUaApiClient apiClient,
        GameDetector gameDetector,
        LocalizationStateService stateService,
        LocalizationCompatibilityService compatService,
        LocalizationInstaller localizationInstaller,
        BackupStore backupStore,
        InstallationStateStore stateStore,
        ILogger logger)
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

        _poller = new ReleaseFeedPoller(_apiClient, _logger);
        _feedCoordinator = new FeedApplicationCoordinator(ApplyFeedPipelineAsync, _poller, _logger);
        _poller.OnFeedCandidate += OnReleaseFeedCandidate;
        _poller.OnPollFailed += OnReleasePollFailed;

        InitializeComponent();
        WireEventHandlers();
        this.Shown += MainForm_Shown;
    }

    private void WireEventHandlers()
    {
        detectGameButton.Click += DetectGameButton_Click;
        browseGameButton.Click += BrowseGameButton_Click;
        installButton.Click += InstallButton_Click;
        restoreOriginalButton.Click += RestoreOriginalButton_Click;
        cancelButton.Click += CancelButton_Click;
        this.FormClosing += MainForm_FormClosing;
    }

    // --- Startup ---

    private async void MainForm_Shown(object? sender, EventArgs e)
    {
        _initializing = true;
        try
        {
            var configLoad = _configStore.Load();
            var config = configLoad.Value ?? new Config();

            SetOperationState(OperationState.LoadingApi);
            SetGameSearching();
            ShowModeLoadingPlaceholder();
            SetMessage("Завантаження даних з сервера...\nПерший запит може зайняти до 30 секунд. Будь ласка, зачекайте.");

            // Timer to show elapsed time during startup
            _startupStartTime = DateTime.Now;
            _startupTimer = new System.Windows.Forms.Timer();
            _startupTimer.Interval = 1000;
            _startupTimer.Tick += (s, args) =>
            {
                var elapsed = (int)(DateTime.Now - _startupStartTime).TotalSeconds;
                SetMessage($"Завантаження даних з сервера... ({elapsed} сек)\nПерший запит може зайняти до 30 секунд. Будь ласка, зачекайте.");
            };
            _startupTimer.Start();

            // Fire-and-forget warmup to pre-warm DNS/TLS cache
            _ = _apiClient.WarmupConnectionAsync();

            var coordinator = new StartupCoordinator(
                () => _apiClient.GetReleasesAsync(),
                (patterns) => _gameDetector.DetectAsync(patterns),
                _logger);

            var result = await coordinator.RunAsync(
                onLocalDetectionComplete: localResult =>
                {
                    if (localResult.GamePath != null)
                    {
                        _gameRoot = localResult.GamePath;
                        SetGameFound(localResult.GamePath, localResult.Source);
                    }
                    else
                    {
                        SetGameNotFound("Локально гру не знайдено. Очікування даних сервера...");
                    }
                },
                onApiComplete: apiResult =>
                {
                    if (apiResult.Success && apiResult.Response != null)
                    {
                        _apiResponse = apiResult.Response;
                        _apiLoadedSuccessfully = true;
                        _apiErrorMessage = null;
                        _apiErrorKind = ApiErrorKind.None;
                        BuildDynamicModes();
                        RestoreLastMode(config);
                    }
                    else
                    {
                        _apiLoadedSuccessfully = false;
                        _apiErrorKind = apiResult.ErrorKind;
                        _apiErrorMessage = apiResult.ErrorMessage;
                        ShowModeFailurePlaceholder();
                        SetMessage(ApiErrorPresentation.GetUserMessage(apiResult.ErrorKind, apiResult.ErrorMessage));
                    }
                },
                onFallbackStarted: () =>
                {
                    SetGameNotFound("Пошук гри за даними сервера...");
                });

            if (result.FinalGamePath != null)
            {
                _gameRoot = result.FinalGamePath;
                SetGameFound(result.FinalGamePath, result.FinalGameSource);
            }
            else
            {
                SetGameNotFound("Гру не знайдено");
            }

            await RefreshStateAsync();
        }
        catch (Exception ex)
        {
            _logger.Error($"Startup error: {ex.Message}");
            SetMessage($"Помилка запуску: {ex.Message}");
        }
        finally
        {
            _startupTimer?.Stop();
            _startupTimer?.Dispose();
            _startupTimer = null;
            _initializing = false;
            SetOperationState(OperationState.Idle);
            if (!_closing)
                _poller.Start(_apiResponse);
        }
    }

    // --- Dynamic modes ---

    private void BuildDynamicModes()
    {
        modesFlowPanel.Controls.Clear();

        var allModes = _apiResponse?.Data?.Modes;
        var installable = DynamicModePolicy.GetInstallableModes(allModes);

        if (installable.Count == 0)
        {
            var label = new Label
            {
                Text = "Наразі немає доступних режимів.",
                AutoSize = true,
                ForeColor = SystemColors.GrayText,
                Margin = new Padding(0)
            };
            modesFlowPanel.Controls.Add(label);
            return;
        }

        foreach (var mode in installable)
        {
            var displayName = DynamicModePolicy.GetDisplayName(mode);
            var releaseLine = DynamicModePolicy.FormatReleaseLine(mode);

            var text = string.IsNullOrEmpty(releaseLine)
                ? displayName
                : $"{displayName}\n{releaseLine}";

            var rb = new RadioButton
            {
                Text = text,
                Tag = mode.Slug,
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 6)
            };
            rb.CheckedChanged += ModeRadioButton_CheckedChanged;
            modesFlowPanel.Controls.Add(rb);
        }
    }

    private void RestoreLastMode(Config config)
    {
        var allModes = _apiResponse?.Data?.Modes;
        var installable = DynamicModePolicy.GetInstallableModes(allModes);
        var selectedSlug = DynamicModePolicy.ResolveInitialSelection(config.LastMode, installable);

        if (selectedSlug != null)
            SelectModeBySlug(selectedSlug);
    }

    private void ShowModeLoadingPlaceholder()
    {
        modesFlowPanel.Controls.Clear();
        var label = new Label
        {
            Text = "Завантаження доступних режимів...",
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(0)
        };
        modesFlowPanel.Controls.Add(label);
    }

    private void ShowModeFailurePlaceholder()
    {
        modesFlowPanel.Controls.Clear();
        var label = new Label
        {
            Text = "Не вдалося завантажити режими.",
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(0)
        };
        modesFlowPanel.Controls.Add(label);
    }

    private void SelectModeBySlug(string slug)
    {
        foreach (Control c in modesFlowPanel.Controls)
        {
            if (c is RadioButton rb && rb.Tag is string tag && tag == slug)
            {
                rb.Checked = true;
                return;
            }
        }

        // Fallback: first available
        foreach (Control c in modesFlowPanel.Controls)
        {
            if (c is RadioButton rb)
            {
                rb.Checked = true;
                return;
            }
        }
    }

    private string? GetSelectedModeSlug()
    {
        string? found = null;
        int count = 0;

        foreach (Control c in modesFlowPanel.Controls)
        {
            if (c is RadioButton rb && rb.Checked)
            {
                found = rb.Tag as string;
                count++;
            }
        }

        if (count > 1)
        {
            _logger.Error($"Ambiguous mode selection: {count} RadioButtons checked");
            return null;
        }

        return found;
    }

    private LocalizationMode? GetSelectedApiMode()
    {
        var slug = GetSelectedModeSlug();
        if (slug == null || _apiResponse?.Data?.Modes == null) return null;
        return _apiResponse.Data.Modes
            .FirstOrDefault(m => string.Equals(m.Slug, slug, StringComparison.Ordinal));
    }

    // --- Detect button ---

    private async void DetectGameButton_Click(object? sender, EventArgs e)
    {
        detectGameButton.Enabled = false;
        var previousGameRoot = _gameRoot;
        SetOperationState(OperationState.DetectingGame);
        SetGameSearching();
        try
        {
            var patterns = _apiResponse?.Data?.InstallPathPatterns;
            var result = await _gameDetector.DetectAsync(patterns);
            if (result.IsFound && result.GamePath != null)
            {
                _gameRoot = result.GamePath;
                SetGameFound(result.GamePath, result.Source);
                await RefreshStateAsync();
            }
            else
            {
                _gameRoot = null;
                SetGameNotFound("Гру не знайдено");
                await RefreshStateAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"Detection error: {ex.Message}");
            if (previousGameRoot != null && GameDetector.ValidateGamePath(previousGameRoot))
            {
                _gameRoot = previousGameRoot;
                SetGameFound(previousGameRoot, null);
                SetMessage($"Помилка пошуку: {ex.Message}");
            }
            else
            {
                _gameRoot = null;
                SetGameNotFound("Помилка пошуку гри");
                SetMessage($"Помилка пошуку: {ex.Message}");
            }
        }
        finally
        {
            detectGameButton.Enabled = true;
            SetOperationState(OperationState.Idle);
        }
    }

    // --- Browse button ---

    private async void BrowseGameButton_Click(object? sender, EventArgs e)
    {
        try
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = "Оберіть папку гри Black Desert Online або її батьківську папку"
            };

            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;

            var resolved = GameDetector.ResolveManualGameRoot(dialog.SelectedPath);

            if (resolved.Status == ManualResolveStatus.Found && resolved.GamePath != null)
            {
                var result = await _gameDetector.ValidateAndSaveManualPathAsync(resolved.GamePath);
                if (result.IsFound && result.GamePath != null)
                {
                    _gameRoot = result.GamePath;
                    SetGameFound(result.GamePath, result.Source);
                    SetMessage("Папку гри успішно визначено.");
                    await RefreshStateAsync();
                }
                else
                {
                    SetManualFailureMessage("У вибраній папці гру не знайдено.");
                }
            }
            else if (resolved.Status == ManualResolveStatus.Ambiguous)
            {
                SetManualFailureMessage("Знайдено кілька папок з грою. Оберіть точну папку гри.");
            }
            else
            {
                SetManualFailureMessage("У вибраній папці гру не знайдено.");
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"Browse error: {ex.Message}");
            SetMessage($"Помилка вибору папки: {ex.Message}");
        }
    }

    private void SetManualFailureMessage(string message)
    {
        if (_gameRoot != null && GameDetector.ValidateGamePath(_gameRoot))
        {
            // Keep existing valid game status, show transient error only
            SetMessage(message);
        }
        else
        {
            SetGameNotFound(message);
        }
    }

    // --- Mode change ---

    private async void ModeRadioButton_CheckedChanged(object? sender, EventArgs e)
    {
        if (_initializing) return;
        if (_suppressModeChanged) return;
        if (sender is not RadioButton rb || !rb.Checked) return;

        try
        {
            var slug = (string)rb.Tag!;
            string? configWarning = null;

            try
            {
                var configLoad = _configStore.Load();
                var config = configLoad.Value ?? new Config();
                config.LastMode = slug;
                await _configStore.SaveAsync(config);
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to save mode config: {ex.Message}");
                configWarning = "Не вдалося зберегти налаштування режиму.";
            }

            SetProgress(0);
            await RefreshStateAsync();

            if (configWarning != null)
            {
                var existingMessage = messageTextBox.Text;
                SetMessage(string.IsNullOrWhiteSpace(existingMessage)
                    ? configWarning
                    : $"{configWarning}{Environment.NewLine}{Environment.NewLine}{existingMessage}");
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"Mode change error: {ex.Message}");
            SetMessage($"Помилка зміни режиму: {ex.Message}");
        }
    }

    // --- Install action ---

    private async void InstallButton_Click(object? sender, EventArgs e)
    {
        try
        {
            await HandleInstallAsync();
        }
        catch (Exception ex)
        {
            _logger.Error($"InstallButton_Click unexpected: {ex.Message}");
            SetMessage($"Помилка: {ex.Message}");
        }
    }

    private async Task HandleInstallAsync()
    {
        if (_operationInProgress) return;

        string? finalMessage = null;

        try
        {
            _operationInProgress = true;
            _poller.Pause();
            _feedCoordinator.BlockUpdates();
            SetOperationState(OperationState.Idle);
            SetActionsEnabled(false, false);
            SetControlsDuringOperation(false);

            if (_gameRoot == null)
            {
                finalMessage = "Гру не знайдено.";
                return;
            }

            if (!_apiLoadedSuccessfully)
            {
                finalMessage = $"Помилка завантаження API: {_apiErrorMessage}";
                return;
            }

            var mode = GetSelectedApiMode();
            if (mode?.Current == null)
            {
                finalMessage = "Актуальний реліз відсутній.";
                return;
            }

            var current = mode.Current;

            var compatResult = _compatService.Check(current);
            if (!compatResult.IsAllowed)
            {
                finalMessage = compatResult.Reason ?? "Операція заблокована.";
                return;
            }

            // Factual state check using INSTALLED mode current
            var installedLoad = _stateStore.Load();
            string? installedModeSlug = null;
            string? installedPublicId = null;
            CurrentRelease? installedModeCurrent = null;

            if (installedLoad.Status == FileLoadStatus.Valid && installedLoad.Value?.Source == "api")
            {
                installedModeSlug = installedLoad.Value.ModeSlug;
                installedPublicId = installedLoad.Value.PublicId;
                var installedApiMode = _apiResponse?.Data?.Modes?
                    .FirstOrDefault(m => string.Equals(m.Slug, installedModeSlug, StringComparison.Ordinal));
                installedModeCurrent = installedApiMode?.Current;
            }

            var gameLocPath = Path.Combine(_gameRoot, "ads", "languagedata_en.loc");
            var factualState = await _stateService.ResolveAsync(installedModeCurrent, gameLocPath);

            var policy = InstallActionPolicy.Evaluate(
                factualState.State, installedModeSlug, installedPublicId,
                mode, current, compatResult, operationInProgress: false);

            if (!policy.CanInstall)
            {
                if (policy.AlreadyInstalledExactTarget)
                    finalMessage = "Цей реліз уже встановлено.";
                else
                    finalMessage = "Встановлення недоступне для поточного стану.";
                return;
            }

            SetMessage("Встановлення локалізації...");
            SetProgress(0);
            SetOperationState(OperationState.Downloading);

            _operationCts = new CancellationTokenSource();
            cancelButton.Enabled = true;

            var service = new LocalizationInstallService(
                _localizationInstaller, _backupStore, _stateStore, _logger, _gameRoot);

            var progress = new Progress<DownloadProgress>(OnDownloadProgress);

            var result = await service.InstallReleaseAsync(
                mode.Slug!, current, progress, _operationCts.Token);

            if (result.IsSuccess)
            {
                SetOperationState(OperationState.Completed);
                finalMessage = "Локалізацію успішно встановлено.";
            }
            else
            {
                SetOperationState(OperationState.Failed);
                _logger.Error($"Install failed: {result.Error} — {result.ErrorMessage}");
                var errorText = MapInstallError(result.Error!.Value);

                if (result.Error == InstallError.RollbackFailed)
                    finalMessage = $"КРИТИЧНО: {errorText}";
                else
                    finalMessage = errorText;
            }
        }
        catch (OperationCanceledException)
        {
            _logger.Info("Install cancelled by user.");
            SetOperationState(OperationState.Cancelled);
            finalMessage = "Встановлення скасовано.";
        }
        catch (Exception ex)
        {
            _logger.Error($"Install error: {ex.Message}");
            SetOperationState(OperationState.Failed);
            finalMessage = $"Помилка операції: {ex.Message}";
        }
        finally
        {
            cancelButton.Enabled = false;
            _operationCts?.Dispose();
            _operationCts = null;
            _operationInProgress = false;
            SetControlsDuringOperation(true);

            try
            {
                try
                {
                    await RefreshStateAsync();
                }
                catch (Exception ex)
                {
                    _logger.Error($"Post-operation refresh failed: {ex.Message}");
                    if (finalMessage == null)
                        finalMessage = $"Не вдалося оновити стан: {ex.Message}";
                    else
                        finalMessage += $"{Environment.NewLine}{Environment.NewLine}Не вдалося оновити стан: {ex.Message}";
                }

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

    // --- Restore Original action ---

    private async void RestoreOriginalButton_Click(object? sender, EventArgs e)
    {
        try
        {
            await HandleRestoreOriginalAsync();
        }
        catch (Exception ex)
        {
            _logger.Error($"RestoreOriginalButton_Click unexpected: {ex.Message}");
            SetMessage($"Помилка: {ex.Message}");
        }
    }

    private async Task HandleRestoreOriginalAsync()
    {
        if (_operationInProgress) return;

        string? finalMessage = null;

        try
        {
            _operationInProgress = true;
            _poller.Pause();
            _feedCoordinator.BlockUpdates();
            SetOperationState(OperationState.Idle);
            SetActionsEnabled(false, false);
            SetControlsDuringOperation(false);

            if (_gameRoot == null)
            {
                finalMessage = "Гру не знайдено.";
                return;
            }

            if (!_apiLoadedSuccessfully || _apiResponse?.Data == null)
            {
                finalMessage = "Дані API недоступні для відновлення оригіналу.";
                return;
            }

            var data = _apiResponse.Data;
            var officialSourceUrl = data.OfficialSourceUrl;
            int? officialPatch = data.OfficialPatch > 0 ? data.OfficialPatch : null;

            SetMessage("Відновлення оригінального файлу...");
            SetProgress(0);
            SetOperationState(OperationState.Restoring);

            _operationCts = new CancellationTokenSource();
            cancelButton.Enabled = true;

            var service = new RestoreOriginalService(
                _localizationInstaller, _backupStore, _stateStore, _logger,
                _gameRoot, officialSourceUrl ?? "", officialPatch);

            var result = await service.RestoreOriginalAsync(_operationCts.Token);

            if (result.IsSuccess)
            {
                SetOperationState(OperationState.Completed);
                finalMessage = "Оригінальні файли відновлено.";
            }
            else
            {
                SetOperationState(OperationState.Failed);
                _logger.Error($"Restore original failed: {result.Error} — {result.ErrorMessage}");
                var errorText = MapRestoreError(result.Error!.Value);

                if (result.Error == RestoreError.RecoveryFailed)
                    finalMessage = $"КРИТИЧНО: {errorText}";
                else
                    finalMessage = errorText;
            }
        }
        catch (OperationCanceledException)
        {
            _logger.Info("Restore Original cancelled by user.");
            SetOperationState(OperationState.Cancelled);
            finalMessage = "Відновлення оригіналу скасовано.";
        }
        catch (Exception ex)
        {
            _logger.Error($"Restore original error: {ex.Message}");
            SetOperationState(OperationState.Failed);
            finalMessage = $"Помилка відновлення: {ex.Message}";
        }
        finally
        {
            cancelButton.Enabled = false;
            _operationCts?.Dispose();
            _operationCts = null;
            _operationInProgress = false;
            SetControlsDuringOperation(true);

            try
            {
                try
                {
                    await RefreshStateAsync();
                }
                catch (Exception ex)
                {
                    _logger.Error($"Post-operation refresh failed: {ex.Message}");
                    if (finalMessage == null)
                        finalMessage = $"Не вдалося оновити стан: {ex.Message}";
                    else
                        finalMessage += $"{Environment.NewLine}{Environment.NewLine}Не вдалося оновити стан: {ex.Message}";
                }

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

    // --- Cancel action ---

    private void CancelButton_Click(object? sender, EventArgs e)
    {
        if (!_operationInProgress || _operationCts == null)
            return;

        cancelButton.Enabled = false;
        SetMessage("Скасування операції...");
        _operationCts.Cancel();
    }

    // --- FormClosing safety ---

    private void MainForm_FormClosing(object? sender, FormClosingEventArgs e)
    {
        if (!_operationInProgress)
        {
            _closing = true;
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

    private void SetControlsDuringOperation(bool enabled)
    {
        detectGameButton.Enabled = enabled;
        browseGameButton.Enabled = enabled;
        foreach (Control c in modesFlowPanel.Controls)
        {
            if (c is RadioButton rb) rb.Enabled = enabled;
        }
    }

    // --- Release feed polling ---

    private async void OnReleaseFeedCandidate(ReleasesResponse candidate)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => OnReleaseFeedCandidate(candidate));
            return;
        }

        try
        {
            if (_closing) return;
            await _feedCoordinator.OnCandidateAsync(candidate);
        }
        catch (Exception ex)
        {
            _logger.Error($"Feed candidate handler error: {ex.Message}");
        }
    }

    private void OnReleasePollFailed(string error)
    {
        // Keep last known good. Log only.
    }

    /// <summary>
    /// Whole-pipeline feed application callback used by FeedApplicationCoordinator.
    /// Returns true only if all stages succeed (API update, mode rebuild, selection, state refresh).
    /// </summary>
    private async Task<bool> ApplyFeedPipelineAsync(ReleasesResponse candidate)
    {
        if (_closing) return false;

        var previousSlug = GetSelectedModeSlug();

        _apiResponse = candidate;
        _apiLoadedSuccessfully = true;
        _apiErrorMessage = null;
        _apiErrorKind = ApiErrorKind.None;

        _suppressModeChanged = true;
        try
        {
            BuildDynamicModes();
            RestoreSelectionAfterFeedUpdate(previousSlug);
        }
        finally
        {
            _suppressModeChanged = false;
        }

        await RefreshStateAsync();
        return true;
    }

    private void RestoreSelectionAfterFeedUpdate(string? previousSlug)
    {
        var allModes = _apiResponse?.Data?.Modes;
        var installable = DynamicModePolicy.GetInstallableModes(allModes);

        if (previousSlug != null && installable.Any(m =>
            string.Equals(m.Slug, previousSlug, StringComparison.Ordinal)))
        {
            SelectModeBySlug(previousSlug);
        }
        else
        {
            var fallback = DynamicModePolicy.ResolveInitialSelection(previousSlug, installable);
            if (fallback != null)
                SelectModeBySlug(fallback);
        }
    }

    // --- Game status presentation ---

    private void SetGameFound(string path, DetectionSource? source)
    {
        gameStatusLabel.Text = source == DetectionSource.Manual
            ? "✓ Гру знайдено вручну"
            : "✓ Гру знайдено";
        gameStatusLabel.ForeColor = SuccessGreen;
        gamePathLabel.Text = path;
    }

    private void SetGameNotFound(string reason)
    {
        gameStatusLabel.Text = reason;
        gameStatusLabel.ForeColor = SystemColors.GrayText;
        gamePathLabel.Text = "";
    }

    private void SetGameSearching()
    {
        gameStatusLabel.Text = "Пошук гри...";
        gameStatusLabel.ForeColor = SystemColors.GrayText;
        gamePathLabel.Text = "";
    }

    // --- Operation state ---

    private void SetOperationState(OperationState state)
    {
        _operationState = state;
        progressLabel.Text = state switch
        {
            OperationState.Idle => "0%",
            OperationState.DetectingGame => "Пошук гри...",
            OperationState.LoadingApi => "Завантаження даних...",
            OperationState.Downloading => "Завантаження...",
            OperationState.Verifying => "Перевірка...",
            OperationState.BackingUp => "Створення резервної копії...",
            OperationState.Installing => "Встановлення...",
            OperationState.Restoring => "Відновлення...",
            OperationState.Completed => "Завершено",
            OperationState.Failed => "Помилка",
            OperationState.Cancelled => "Скасовано",
            _ => "0%"
        };

        bool indeterminate = state is OperationState.LoadingApi
            or OperationState.DetectingGame
            or OperationState.Verifying
            or OperationState.BackingUp
            or OperationState.Installing
            or OperationState.Restoring;

        if (indeterminate)
        {
            progressBar.Style = ProgressBarStyle.Marquee;
            progressBar.MarqueeAnimationSpeed = 30;
        }
        else
        {
            progressBar.Style = ProgressBarStyle.Continuous;
            progressBar.MarqueeAnimationSpeed = 0;
        }
    }

    private void OnDownloadProgress(DownloadProgress progress)
    {
        var percent = progress.Percentage;
        if (percent.HasValue)
        {
            var clamped = (int)Math.Clamp(Math.Round(percent.Value), 0, 100);
            progressBar.Value = clamped;
            progressLabel.Text = $"{clamped}%";
        }
    }

    // --- State refresh ---

    private async Task RefreshStateAsync()
    {
        SetActionsEnabled(false, false);

        if (_gameRoot == null)
        {
            SetLocalizationStateText("Не визначено");
            SetInstalledInfo("");
            SetSelectedInfo("");
            _lastResolvedState = LocalizationState.NotInstalled;
            if (!_apiLoadedSuccessfully)
                SetMessage(ApiErrorPresentation.GetUserMessage(_apiErrorKind, _apiErrorMessage));
            else
                SetMessage("Гру не знайдено. Натисніть \"Знайти гру\" або оберіть папку вручну.");
            return;
        }

        if (!_apiLoadedSuccessfully)
        {
            SetLocalizationStateText("Не визначено");
            SetInstalledInfo("");
            SetSelectedInfo("");
            _lastResolvedState = LocalizationState.NotInstalled;
            SetMessage(ApiErrorPresentation.GetUserMessage(_apiErrorKind, _apiErrorMessage));
            SetActionsEnabled(false, !_operationInProgress);
            return;
        }

        // Resolve factual installed mode
        var installedLoad = _stateStore.Load();
        string? installedModeSlug = null;
        string? installedPublicId = null;
        DateTimeOffset? installedAt = null;
        string? installedModeDisplayName = null;

        if (installedLoad.Status == FileLoadStatus.Valid && installedLoad.Value?.Source == "api")
        {
            installedModeSlug = installedLoad.Value.ModeSlug;
            installedPublicId = installedLoad.Value.PublicId;
            installedAt = installedLoad.Value.InstalledAt;

            var installedApiMode = _apiResponse?.Data?.Modes?
                .FirstOrDefault(m => string.Equals(m.Slug, installedModeSlug, StringComparison.Ordinal));
            installedModeDisplayName = installedApiMode?.PublicName ?? installedModeSlug;
        }

        // Factual LocalizationState uses INSTALLED mode's current
        CurrentRelease? installedModeCurrent = null;
        if (installedModeSlug != null)
        {
            var installedApiMode = _apiResponse?.Data?.Modes?
                .FirstOrDefault(m => string.Equals(m.Slug, installedModeSlug, StringComparison.Ordinal));
            installedModeCurrent = installedApiMode?.Current;
        }

        var gameLocPath = Path.Combine(_gameRoot, "ads", "languagedata_en.loc");
        var stateResult = await _stateService.ResolveAsync(installedModeCurrent, gameLocPath);
        _lastResolvedState = stateResult.State;
        ApplyLocalizationStatePresentation(stateResult.State);

        // Installed info display
        if (installedLoad.Status == FileLoadStatus.Valid && installedLoad.Value?.Source == "api")
        {
            var dateStr = installedAt.HasValue
                ? installedAt.Value.ToLocalTime().ToString("dd.MM.yyyy HH:mm")
                : "";
            var info = $"Встановлено: {installedModeDisplayName ?? "невідомо"}";
            if (installedLoad.Value.Version > 0)
                info += $" • v{installedLoad.Value.Version}";
            if (dateStr != "")
                info += $" • {dateStr}";
            SetInstalledInfo(info);
        }
        else if (installedLoad.Status == FileLoadStatus.Missing
            || (installedLoad.Status == FileLoadStatus.Valid && installedLoad.Value?.Source == "official"))
        {
            SetInstalledInfo("Локалізацію не встановлено");
        }
        else
        {
            SetInstalledInfo("");
        }

        // Selected mode details
        var selectedMode = GetSelectedApiMode();
        var selectedCurrent = selectedMode?.Current;

        if (selectedCurrent != null)
        {
            var selName = DynamicModePolicy.GetDisplayName(selectedMode!);
            var selLine = DynamicModePolicy.FormatReleaseLine(selectedMode!);
            SetSelectedInfo(string.IsNullOrEmpty(selLine)
                ? $"Обрано: {selName}"
                : $"Обрано: {selName} • {selLine}");
        }
        else
        {
            SetSelectedInfo("");
        }

        // Installed marker on matching RadioButton
        UpdateInstalledMarkers(installedModeSlug, installedPublicId);

        // Diagnostics
        string? diagnostic = stateResult.Error;

        var compatResult = _compatService.Check(selectedCurrent);
        if (diagnostic == null && !compatResult.IsAllowed && compatResult.Reason != null)
            diagnostic = compatResult.Reason;

        if (diagnostic == null && stateResult.State == LocalizationState.Corrupted)
            diagnostic = "Файл локалізації пошкоджено. Спробуйте встановити знову.";

        // Exact-installed informational message
        if (diagnostic == null)
        {
            var policy = InstallActionPolicy.Evaluate(
                stateResult.State, installedModeSlug, installedPublicId,
                selectedMode, selectedCurrent, compatResult, _operationInProgress);

            if (policy.AlreadyInstalledExactTarget)
                diagnostic = "Цей реліз уже встановлено.";
        }

        SetMessage(diagnostic ?? "");

        // Action availability
        var actionPolicy = InstallActionPolicy.Evaluate(
            stateResult.State, installedModeSlug, installedPublicId,
            selectedMode, selectedCurrent, compatResult, _operationInProgress);

        SetActionsEnabled(actionPolicy.CanInstall, actionPolicy.CanRestoreOriginal);
    }

    private void UpdateInstalledMarkers(string? installedModeSlug, string? installedPublicId)
    {
        foreach (Control c in modesFlowPanel.Controls)
        {
            if (c is not RadioButton rb) continue;

            var slug = rb.Tag as string;

            // Find the API mode for this RadioButton
            var mode = _apiResponse?.Data?.Modes?
                .FirstOrDefault(m => string.Equals(m.Slug, slug, StringComparison.Ordinal));

            // Exact installed: same ModeSlug AND same PublicId of current release
            bool isExactInstalled = InstallActionPolicy.IsExactInstalledTarget(
                installedModeSlug, installedPublicId, mode);

            if (mode != null)
            {
                var displayName = DynamicModePolicy.GetDisplayName(mode);
                var releaseLine = DynamicModePolicy.FormatReleaseLine(mode);

                var text = string.IsNullOrEmpty(releaseLine)
                    ? displayName
                    : $"{displayName}\n{releaseLine}";

                if (isExactInstalled)
                    text += "\n✓ Встановлено";

                rb.Text = text;
            }
        }
    }

    private static string GetStateDisplayText(LocalizationState state) => state switch
    {
        LocalizationState.NotInstalled => "Локалізацію не встановлено",
        LocalizationState.UpToDate => "✓ Встановлена локалізація актуальна",
        LocalizationState.UpdateAvailable => "Доступна новіша версія встановленої локалізації",
        LocalizationState.WaitingForRelease => "Очікується актуальний реліз",
        LocalizationState.InstalledVersionUnknown => "Не вдалося визначити встановлену версію",
        LocalizationState.Corrupted => "Файл локалізації пошкоджено",
        _ => "Не визначено"
    };

    // --- Presentation helpers ---

    public void SetGamePathText(string text) => gamePathLabel.Text = text;

    public void SetLocalizationStateText(string text)
    {
        localizationStateLabel.Text = text;
        localizationStateLabel.ForeColor = SystemColors.ControlText;
    }

    private void ApplyLocalizationStatePresentation(LocalizationState state)
    {
        localizationStateLabel.Text = GetStateDisplayText(state);
        localizationStateLabel.ForeColor = state switch
        {
            LocalizationState.UpToDate => SuccessGreen,
            LocalizationState.Corrupted => Color.DarkRed,
            _ => SystemColors.ControlText
        };
    }

    public void SetInstalledInfo(string text) => installedInfoLabel.Text = text;

    public void SetSelectedInfo(string text) => detailsLabel.Text = text;

    public void SetProgress(int percent)
    {
        progressBar.Value = Math.Clamp(percent, 0, 100);
        progressLabel.Text = $"{progressBar.Value}%";
    }

    public void SetMessage(string text) => messageTextBox.Text = text;

    public void SetActionsEnabled(bool install, bool restoreOriginal)
    {
        installButton.Enabled = install;
        restoreOriginalButton.Enabled = restoreOriginal;
    }

    private static string MapInstallError(InstallError error) => error switch
    {
        InstallError.InvalidGamePath => "Шлях до гри недійсний або файл локалізації відсутній.",
        InstallError.InvalidRelease => "Метадані релізу пошкоджено або неповні.",
        InstallError.Incompatible => "Реліз не сумісний з поточним офіційним патчем гри.",
        InstallError.DownloadFailed => "Не вдалося завантажити файл локалізації. Перевірте з'єднання з Інтернетом.",
        InstallError.OriginalSnapshotFailed => "Не вдалося створити резервну копію оригінального файлу.",
        InstallError.PreOperationStateFailed => "Стан встановлення пошкоджено. Спробуйте перезапустити програму.",
        InstallError.BackupFailed => "Не вдалося створити точку відновлення.",
        InstallError.ReplaceFailed => "Не вдалося замінити файл локалізації у папці гри.",
        InstallError.VerificationFailed => "Перевірка встановленого файлу не пройдена. Файл може бути пошкоджено.",
        InstallError.StateSaveFailed => "Не вдалося зберегти стан встановлення. Зміни відкочено.",
        InstallError.RollbackFailed => "Не вдалося повністю відкотити зміни. Перевірте файли гри та журнал.",
        _ => "Невідома помилка встановлення."
    };

    private static string MapRestoreError(RestoreError error) => error switch
    {
        RestoreError.InvalidGamePath => "Шлях до гри недійсний або файл локалізації відсутній.",
        RestoreError.SourceMissing => "Вихідний файл відсутній.",
        RestoreError.SnapshotCorrupted => "Резервна копія пошкоджена.",
        RestoreError.BackupIo => "Не вдалося створити резервну копію поточного стану.",
        RestoreError.OfficialDownloadFailed => "Не вдалося завантажити оригінальний файл з сервера.",
        RestoreError.FallbackNotAllowed => "Відновлення з локальної копії неможливе (патч не збігається або копія відсутня).",
        RestoreError.PatchMismatch => "Патч локальної копії не збігається з поточним офіційним патчем.",
        RestoreError.ReplaceFailed => "Не вдалося замінити файл локалізації у папці гри.",
        RestoreError.VerificationFailed => "Перевірка відновленого файлу не пройдена.",
        RestoreError.StateSaveFailed => "Не вдалося зберегти стан встановлення після відновлення.",
        RestoreError.RecoveryFailed => "Не вдалося повністю відкотити зміни. Перевірте файли гри та журнал.",
        RestoreError.RestorePointNotFound => "Резервну копію не знайдено.",
        RestoreError.RestorePointInvalid => "Резервна копія пошкоджена або непридатна для відновлення.",
        RestoreError.StateRestoreFailed => "Не вдалося відновити стан локалізації. Попередній стан було повернуто.",
        _ => "Невідома помилка відновлення."
    };
}
