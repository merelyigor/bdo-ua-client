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

    private void ApplyTheme()
    {
        mainLayoutPanel.Width = Math.Max(0, rootScrollPanel.ClientSize.Width);
        BackColor = UiTheme.Background;
        ForeColor = UiTheme.PrimaryText;
        Font = new Font("Segoe UI", 9F);

        foreach (Control control in Controls)
            ApplyContainerTheme(control);

        gameGroupBox.BackColor = UiTheme.PanelBackground;
        gameGroupBox.ForeColor = UiTheme.PrimaryText;
        modeGroupBox.BackColor = UiTheme.Background;
        modeGroupBox.ForeColor = UiTheme.PrimaryText;
        headerTitleLabel.ForeColor = UiTheme.PrimaryText;
        headerSubtitleLabel.ForeColor = UiTheme.SecondaryText;
        headerAccentLine.BackColor = UiTheme.Accent;
        gameSectionCaptionLabel.ForeColor = UiTheme.SecondaryText;
        modeSectionCaptionLabel.ForeColor = UiTheme.SecondaryText;
        modesFlowPanel.BackColor = Color.Transparent;
        gameStatusLabel.ForeColor = UiTheme.SecondaryText;
        gamePathLabel.ForeColor = UiTheme.SecondaryText;
        progressLabel.ForeColor = UiTheme.SecondaryText;
        versionLabel.ForeColor = UiTheme.SecondaryText;

        UiTheme.StyleSecondaryButton(detectGameButton);
        UiTheme.StyleSecondaryButton(browseGameButton);
        UiTheme.StyleAccentSecondaryButton(restoreOriginalButton);
        UiTheme.StyleDestructiveButton(cancelButton);
        UiTheme.StyleAccentSecondaryButton(updateButton);
        UiTheme.StyleSecondaryButton(logsButton);

        progressBar.BackColor = UiTheme.ControlBackground;
        progressBar.ForeColor = UiTheme.Accent;
        RefreshModeCardLayout();
    }

    private static void ApplyContainerTheme(Control control)
    {
        if (control is Panel)
            control.BackColor = Color.Transparent;

        foreach (Control child in control.Controls)
            ApplyContainerTheme(child);
    }

    private void RootScrollPanel_Resize(object? sender, EventArgs e)
    {
        mainLayoutPanel.Width = Math.Max(0, rootScrollPanel.ClientSize.Width);
        ScheduleContentFit();
    }

    private void ScheduleContentFit()
    {
        if (_contentFitScheduled || _closing || IsDisposed || !IsHandleCreated)
            return;

        _contentFitScheduled = true;
        BeginInvoke(new Action(() =>
        {
            _contentFitScheduled = false;
            EnsureContentFitsWindow();
        }));
    }

    private void EnsureContentFitsWindow()
    {
        if (_contentFitInProgress || IsDisposed)
            return;

        _contentFitInProgress = true;
        try
        {
            rootScrollPanel.PerformLayout();
            mainLayoutPanel.PerformLayout();

            var preferredHeight = mainLayoutPanel.GetPreferredSize(
                new Size(rootScrollPanel.ClientSize.Width, 0)).Height;
            var formChromeHeight = Height - ClientSize.Height;
            var workingArea = Screen.FromControl(this).WorkingArea;
            var safetyMargin = 16;
            var maxClientHeight = Math.Max(
                MinimumSize.Height,
                workingArea.Height - formChromeHeight - safetyMargin);
            var requiredClientHeight = Math.Min(maxClientHeight, preferredHeight);

            if (ClientSize.Height < requiredClientHeight)
                ClientSize = new Size(ClientSize.Width, requiredClientHeight);
        }
        finally
        {
            _contentFitInProgress = false;
        }
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

    private void ModesFlowPanel_Resize(object? sender, EventArgs e)
    {
        RefreshModeCardLayout();
        ScheduleContentFit();
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
                        RestoreInitialMode(config);
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
            {
                _poller.Start(_apiResponse);
                await StartupUpdateLifecycleCoordinator.RunAsync(
                    RunStartupLifecycleMaintenanceAsync,
                    () => _closing,
                    StartBackgroundUpdateCheck,
                    ex => _logger.Warning($"Startup lifecycle maintenance failed: {ex.Message}"));
            }
        }
    }

    private async Task RunStartupLifecycleMaintenanceAsync()
    {
        try
        {
            await Task.Run(() => _updateLifecycle.RunStartupMaintenance());
        }
        catch (Exception ex)
        {
            _logger.Warning($"Startup lifecycle maintenance failed: {ex.Message}");
        }
    }

    // --- Dynamic modes ---

    private void BuildDynamicModes()
    {
        ClearModeControls();

        var allModes = _apiResponse?.Data?.Modes;
        var installable = DynamicModePolicy.GetInstallableModes(allModes);

        if (installable.Count == 0)
        {
            var localPatch = AdsFilesPatchReader.TryReadPatch(_gameRoot);
            var officialPatch = _apiResponse?.Data?.OfficialPatch;
            var patch = localPatch ?? (officialPatch > 0 ? officialPatch : null);
            var label = new Label
            {
                Text = patch.HasValue
                    ? $"Для патча {patch.Value} поки немає доступних режимів локалізації."
                    : "Наразі немає доступних режимів.",
                AutoSize = true,
                ForeColor = UiTheme.SecondaryText,
                BackColor = Color.Transparent,
                Margin = new Padding(0)
            };
            modesFlowPanel.Controls.Add(label);
            ScheduleContentFit();
            return;
        }

        foreach (var mode in installable)
        {
            var card = new LocalizationModeCard(mode);
            card.SelectionRequested += ModeCard_SelectionRequested;
            card.ActionRequested += ModeCard_ActionRequested;
            modesFlowPanel.Controls.Add(card);
        }
        RefreshModeCardLayout();
        ScheduleContentFit();
    }

    private void RefreshModeCardLayout()
    {
        if (modesFlowPanel == null) return;
        var availableWidth = modeGroupBox.ClientSize.Width - modesFlowPanel.Margin.Horizontal;
        if (availableWidth <= 0)
            availableWidth = ClientSize.Width - mainLayoutPanel.Padding.Horizontal;
        modesFlowPanel.Width = Math.Max(UiTheme.Scale(modesFlowPanel, 240), availableWidth);
        var width = Math.Max(UiTheme.Scale(modesFlowPanel, 240), modesFlowPanel.ClientSize.Width - modesFlowPanel.Padding.Horizontal);
        var columns = width >= UiTheme.Scale(modesFlowPanel, 900) ? 3
            : width >= UiTheme.Scale(modesFlowPanel, 620) ? 2 : 1;
        var gap = UiTheme.Scale(modesFlowPanel, 16);
        var cardWidth = Math.Max(UiTheme.Scale(modesFlowPanel, 240), (width - gap * (columns - 1)) / columns);
        var cardHeight = UiTheme.Scale(modesFlowPanel, 220);
        var cards = modesFlowPanel.Controls.OfType<LocalizationModeCard>().ToList();
        foreach (var card in cards)
        {
            card.Width = cardWidth;
            card.Height = Math.Max(cardHeight, card.GetPreferredSize(new Size(cardWidth, 0)).Height);
            cardHeight = Math.Max(cardHeight, card.Height);
        }
        var cardCount = cards.Count;
        var rows = Math.Max(1, (int)Math.Ceiling(cardCount / (double)columns));
        for (var index = 0; index < cards.Count; index++)
        {
            var column = index % columns;
            var row = index / columns;
            cards[index].Bounds = new Rectangle(
                modesFlowPanel.Padding.Left + column * (cardWidth + gap),
                modesFlowPanel.Padding.Top + row * (cardHeight + gap),
                cardWidth,
                cardHeight);
        }
        modesFlowPanel.Height = cardCount == 0
            ? modesFlowPanel.Padding.Top + UiTheme.Scale(modesFlowPanel, 56)
            : modesFlowPanel.Padding.Top + rows * cardHeight + (rows - 1) * gap;
        modeGroupBox.Height = modeSectionCaptionLabel.PreferredHeight
            + modeSectionCaptionLabel.Margin.Vertical
            + modesFlowPanel.Height;
        modesFlowPanel.PerformLayout();
    }

    private void RestoreInitialMode(Config config)
    {
        var allModes = _apiResponse?.Data?.Modes;
        var installable = DynamicModePolicy.GetInstallableModes(allModes);
        var installedModeSlug = GetInstalledModeSlugForInitialSelection();
        var selectedSlug = DynamicModePolicy.ResolveInitialSelection(
            installedModeSlug, config.LastMode, installable);

        if (selectedSlug != null)
            SelectModeBySlug(selectedSlug);
    }

    private string? GetInstalledModeSlugForInitialSelection()
    {
        var installedLoad = _stateStore.Load();
        if (installedLoad.Status != FileLoadStatus.Valid
            || installedLoad.Value?.Source != InstallationSource.Api
            || string.IsNullOrWhiteSpace(installedLoad.Value.ModeSlug))
        {
            return null;
        }

        return installedLoad.Value.ModeSlug;
    }

    private void ShowModeLoadingPlaceholder()
    {
        ClearModeControls();
        var label = new Label
        {
            Text = "Завантаження доступних режимів...",
            AutoSize = true,
            ForeColor = UiTheme.SecondaryText,
            BackColor = Color.Transparent,
            Margin = new Padding(0)
        };
        modesFlowPanel.Controls.Add(label);
        ScheduleContentFit();
    }

    private void ShowModeFailurePlaceholder()
    {
        ClearModeControls();
        var label = new Label
        {
            Text = "Не вдалося завантажити режими.",
            AutoSize = true,
            ForeColor = UiTheme.SecondaryText,
            BackColor = Color.Transparent,
            Margin = new Padding(0)
        };
        modesFlowPanel.Controls.Add(label);
        ScheduleContentFit();
    }

    private void ClearModeControls()
    {
        foreach (var card in modesFlowPanel.Controls.OfType<LocalizationModeCard>().ToList())
            card.Dispose();
        modesFlowPanel.Controls.Clear();
    }

    private void SelectModeBySlug(string slug)
    {
        var selected = modesFlowPanel.Controls
            .OfType<LocalizationModeCard>()
            .FirstOrDefault(card => string.Equals(card.ModeSlug, slug, StringComparison.Ordinal));
        selected ??= modesFlowPanel.Controls.OfType<LocalizationModeCard>().FirstOrDefault();
        foreach (var card in modesFlowPanel.Controls.OfType<LocalizationModeCard>())
            card.IsSelected = ReferenceEquals(card, selected);
    }

    private string? GetSelectedModeSlug()
    {
        string? found = null;
        int count = 0;

        foreach (var card in modesFlowPanel.Controls.OfType<LocalizationModeCard>())
        {
            if (card.IsSelected)
            {
                found = card.ModeSlug;
                count++;
            }
        }

        if (count > 1)
        {
            _logger.Error($"Ambiguous mode selection: {count} mode cards selected");
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

    private async void ModeCard_SelectionRequested(object? sender, EventArgs e)
    {
        if (_initializing) return;
        if (_suppressModeChanged) return;
        if (sender is not LocalizationModeCard card || !card.Enabled) return;

        try
        {
            var previousSlug = GetSelectedModeSlug();
            SelectModeBySlug(card.ModeSlug);
            var slug = card.ModeSlug;
            if (string.Equals(previousSlug, slug, StringComparison.Ordinal)) return;
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
                var existingMessage = operationMessageLabel.Text;
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

    private async void ModeCard_ActionRequested(object? sender, EventArgs e)
    {
        if (_initializing || _suppressModeChanged || _operationInProgress || sender is not LocalizationModeCard card)
            return;

        try
        {
            var previousSlug = GetSelectedModeSlug();
            SelectModeBySlug(card.ModeSlug);
            if (!string.Equals(previousSlug, card.ModeSlug, StringComparison.Ordinal))
            {
                try
                {
                    var configLoad = _configStore.Load();
                    var config = configLoad.Value ?? new Config();
                    config.LastMode = card.ModeSlug;
                    await _configStore.SaveAsync(config);
                }
                catch (Exception ex)
                {
                    _logger.Warning($"Failed to save selected mode: {ex.Message}");
                }
                await RefreshStateAsync();
            }
            await HandleInstallAsync();
        }
        catch (Exception ex)
        {
            _logger.Error($"Mode card action error: {ex.Message}");
            SetMessage($"Помилка операції: {ex.Message}");
        }
    }

    // --- Install action ---

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
            SetActionsEnabled(false);
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

            if (installedLoad.Status == FileLoadStatus.Valid && installedLoad.Value?.Source == InstallationSource.Api)
            {
                installedModeSlug = installedLoad.Value.ModeSlug;
                installedPublicId = installedLoad.Value.PublicId;
                var installedApiMode = _apiResponse?.Data?.Modes?
                    .FirstOrDefault(m => string.Equals(m.Slug, installedModeSlug, StringComparison.Ordinal));
                installedModeCurrent = installedApiMode?.Current;
            }

            var gameLocPath = GamePaths.GetLocalizationFilePath(_gameRoot);
            var factualState = await _stateService.ResolveAsync(installedModeCurrent, gameLocPath, gameRoot: _gameRoot);

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
            cancelButton.Visible = true;
            cancelButton.Enabled = true;
            UpdateCancelButtonVisibility(_operationState);

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
            cancelButton.Visible = false;
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
            SetActionsEnabled(false);
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
            cancelButton.Visible = true;
            cancelButton.Enabled = true;
            UpdateCancelButtonVisibility(_operationState);

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
            cancelButton.Visible = false;
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

    private void SetControlsDuringOperation(bool enabled)
    {
        detectGameButton.Enabled = enabled;
        browseGameButton.Enabled = enabled;
        foreach (var card in modesFlowPanel.Controls.OfType<LocalizationModeCard>())
            card.Enabled = enabled;
        if (_apiLoadedSuccessfully)
            ApplyModeCardPresentations(_lastResolvedState, _lastInstalledModeSlug, _lastInstalledPublicId);
        RefreshUpdateButtonPresentation();
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
        gameStatusLabel.ForeColor = UiTheme.Success;
        gamePathLabel.Text = path;
        detectGameButton.Text = "Перевірити";
    }

    private void SetGameNotFound(string reason)
    {
        gameStatusLabel.Text = reason;
        gameStatusLabel.ForeColor = UiTheme.SecondaryText;
        gamePathLabel.Text = "";
        detectGameButton.Text = "Знайти автоматично";
    }

    private void SetGameSearching()
    {
        gameStatusLabel.Text = "Пошук гри...";
        gameStatusLabel.ForeColor = UiTheme.SecondaryText;
        gamePathLabel.Text = "";
        detectGameButton.Text = "Пошук...";
    }

    // --- Operation state ---

    private static bool IsCancellableState(OperationState state) => state is OperationState.Downloading or OperationState.Verifying or OperationState.BackingUp or OperationState.Installing or OperationState.Restoring;

    private void UpdateCancelButtonVisibility(OperationState state)
    {
        var canCancel = _operationInProgress && _operationCts != null && IsCancellableState(state);
        // Keep visible but disabled while cancellation is being processed
        if (_operationCts?.IsCancellationRequested == true && canCancel)
            cancelButton.Visible = true;
        else
            cancelButton.Visible = canCancel;
    }

    private void SetOperationState(OperationState state)
    {
        _operationState = state;
        operationStrip.Visible = state != OperationState.Idle || !string.IsNullOrWhiteSpace(operationMessageLabel.Text);
        UpdateCancelButtonVisibility(state);
        progressBar.IndicatorColor = state switch
        {
            OperationState.Completed => UiTheme.Success,
            OperationState.Failed => UiTheme.Error,
            OperationState.Cancelled => UiTheme.SecondaryText,
            _ => UiTheme.Accent
        };
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
            if (state == OperationState.Completed)
                progressBar.Value = 100;
            else if (state == OperationState.Idle)
                progressBar.Value = 0;
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
        SetActionsEnabled(false);

        if (_gameRoot == null)
        {
            _lastResolvedState = LocalizationState.NotInstalled;
            if (!_apiLoadedSuccessfully)
                SetMessage(ApiErrorPresentation.GetUserMessage(_apiErrorKind, _apiErrorMessage));
            else
                SetMessage("Гру не знайдено. Натисніть \"Знайти автоматично\" або оберіть папку.");
            ScheduleContentFit();
            return;
        }

        if (!_apiLoadedSuccessfully)
        {
            _lastResolvedState = LocalizationState.NotInstalled;
            SetMessage(ApiErrorPresentation.GetUserMessage(_apiErrorKind, _apiErrorMessage));
            SetActionsEnabled(!_operationInProgress);
            ScheduleContentFit();
            return;
        }

        // Resolve factual installed mode
        var installedLoad = _stateStore.Load();
        string? installedModeSlug = null;
        string? installedPublicId = null;

        if (installedLoad.Status == FileLoadStatus.Valid && installedLoad.Value?.Source == InstallationSource.Api)
        {
            installedModeSlug = installedLoad.Value.ModeSlug;
            installedPublicId = installedLoad.Value.PublicId;
        }

        // Factual LocalizationState uses INSTALLED mode's current
        CurrentRelease? installedModeCurrent = null;
        if (installedModeSlug != null)
        {
            var installedApiMode = _apiResponse?.Data?.Modes?
                .FirstOrDefault(m => string.Equals(m.Slug, installedModeSlug, StringComparison.Ordinal));
            installedModeCurrent = installedApiMode?.Current;
        }

        var gameLocPath = GamePaths.GetLocalizationFilePath(_gameRoot);
        var stateResult = await _stateService.ResolveAsync(installedModeCurrent, gameLocPath, gameRoot: _gameRoot);
        _lastResolvedState = stateResult.State;
        _lastInstalledModeSlug = installedModeSlug;
        _lastInstalledPublicId = installedPublicId;
        var selectedMode = GetSelectedApiMode();
        var selectedCurrent = selectedMode?.Current;

        var hasInstalledApiState = installedLoad.Status == FileLoadStatus.Valid
            && installedLoad.Value?.Source == InstallationSource.Api;
        var sameInstalledModeSelected = hasInstalledApiState
            && selectedMode?.Slug != null
            && string.Equals(installedModeSlug, selectedMode.Slug, StringComparison.Ordinal);

        // Installed marker on the matching mode card
        UpdateInstalledMarkers(installedModeSlug, installedPublicId);

        // Diagnostics
        string? diagnostic = stateResult.Error;

        var compatResult = _compatService.Check(selectedCurrent);
        if (diagnostic == null && !compatResult.IsAllowed && compatResult.Reason != null)
            diagnostic = compatResult.Reason;

        if (diagnostic == null && stateResult.State == LocalizationState.Corrupted)
            diagnostic = "Файл локалізації пошкоджено. Спробуйте встановити знову.";

        if (diagnostic == null && !IsCriticalHeadlineState(stateResult))
        {
            var policy = InstallActionPolicy.Evaluate(
                stateResult.State, installedModeSlug, installedPublicId,
                selectedMode, selectedCurrent, compatResult, _operationInProgress);

            if (policy.AlreadyInstalledExactTarget && stateResult.State == LocalizationState.UpToDate)
                diagnostic = "Встановлена остання доступна версія.";
            else if (!hasInstalledApiState && selectedCurrent != null)
                diagnostic = "Натисніть «Встановити», щоб встановити обраний режим.";
            else if (hasInstalledApiState && selectedCurrent != null)
                diagnostic = sameInstalledModeSelected
                    ? "Натисніть «Оновити», щоб оновити локалізацію."
                    : "Натисніть «Встановити», щоб перейти на обраний режим.";
        }

        // Ordinary card states explain themselves. The compact strip is reserved for
        // operations and important diagnostics that require global attention.
        SetMessage(IsCriticalHeadlineState(stateResult) || !compatResult.IsAllowed
            ? diagnostic ?? ""
            : "");

        // Action availability
        var actionPolicy = InstallActionPolicy.Evaluate(
            stateResult.State, installedModeSlug, installedPublicId,
            selectedMode, selectedCurrent, compatResult, _operationInProgress);

        SetActionsEnabled(actionPolicy.CanRestoreOriginal);
        ApplyModeCardPresentations(stateResult.State, installedModeSlug, installedPublicId);
        ScheduleContentFit();
    }

    private void UpdateInstalledMarkers(string? installedModeSlug, string? installedPublicId)
    {
        foreach (var card in modesFlowPanel.Controls.OfType<LocalizationModeCard>())
        {
            var mode = card.Mode;

            // Exact installed: same ModeSlug AND same PublicId of current release
            bool isExactInstalled = InstallActionPolicy.IsExactInstalledTarget(
                installedModeSlug, installedPublicId, mode);

            if (mode != null)
            {
                card.IsInstalled = isExactInstalled;
            }
        }
    }

    private void ApplyModeCardPresentations(
        LocalizationState factualState,
        string? installedModeSlug,
        string? installedPublicId)
    {
        var selectedSlug = GetSelectedModeSlug();
        foreach (var card in modesFlowPanel.Controls.OfType<LocalizationModeCard>())
        {
            var compatibility = _compatService.Check(card.Mode.Current);
            card.ApplyPresentation(ModeCardPresentationPolicy.Create(
                factualState, installedModeSlug, installedPublicId, card.Mode, compatibility,
                _operationInProgress,
                _operationInProgress && string.Equals(selectedSlug, card.ModeSlug, StringComparison.Ordinal)));
        }
        RefreshModeCardLayout();
    }

    private static bool IsCriticalHeadlineState(LocalizationStateResult result)
    {
        return result.PatchTransition != LocalizationPatchTransition.None
            || result.State is LocalizationState.Corrupted
                or LocalizationState.WaitingForRelease
                or LocalizationState.InstalledVersionUnknown;
    }

    public void SetProgress(int percent)
    {
        progressBar.Value = Math.Clamp(percent, 0, 100);
        progressLabel.Text = $"{progressBar.Value}%";
    }

    public void SetMessage(string text)
    {
        operationMessageLabel.Text = text;
        operationStrip.Visible = _operationState != OperationState.Idle || !string.IsNullOrWhiteSpace(text);
        operationStrip.SurfaceBorderColor = _operationState == OperationState.Failed ? UiTheme.Error
            : _operationState == OperationState.Completed ? UiTheme.Success
            : UiTheme.Border;
        operationStrip.Invalidate();
        ScheduleContentFit();
    }

    public void SetActionsEnabled(bool restoreOriginal)
    {
        restoreOriginalButton.Enabled = restoreOriginal;
        UiTheme.RefreshButtonState(restoreOriginalButton);
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

    internal static Image BuildLogsIcon()
    {
        var bmp = new Bitmap(16, 16, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Transparent);
            using var tab = new SolidBrush(Color.FromArgb(255, 180, 130, 20));
            using var body = new SolidBrush(Color.FromArgb(255, 218, 165, 32));
            using var highlight = new Pen(Color.FromArgb(100, 255, 255, 255), 1);
            g.FillRectangle(tab, 1, 2, 6, 3);
            g.FillRectangle(body, 0, 4, 15, 11);
            g.DrawLine(highlight, 1, 5, 14, 5);
        }
        return bmp;
    }
}
