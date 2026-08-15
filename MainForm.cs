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

    private string? _gameRoot;
    private ReleasesResponse? _apiResponse;
    private bool _apiLoadedSuccessfully;
    private string? _apiErrorMessage;
    private bool _initializing;
    private bool _operationInProgress;
    private LocalizationState _lastResolvedState;
    private OperationState _operationState = OperationState.Idle;
    private CancellationTokenSource? _operationCts;

    private static readonly string[] KnownModeSlugs = new[]
    {
        "full-ukrainian", "full-ukrainian-bosia", "english-items"
    };

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

        InitializeComponent();
        WireEventHandlers();
        this.Shown += MainForm_Shown;
    }

    private void WireEventHandlers()
    {
        detectGameButton.Click += DetectGameButton_Click;
        browseGameButton.Click += BrowseGameButton_Click;
        fullUkrainianRadioButton.CheckedChanged += ModeRadioButton_CheckedChanged;
        bosiaRadioButton.CheckedChanged += ModeRadioButton_CheckedChanged;
        englishItemsRadioButton.CheckedChanged += ModeRadioButton_CheckedChanged;
        installButton.Click += InstallButton_Click;
        updateButton.Click += UpdateButton_Click;
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
            // 1. Load config and restore last_mode
            var configLoad = _configStore.Load();
            var config = configLoad.Value ?? new Config();
            RestoreLastMode(config);

            // 2. Load API
            SetOperationState(OperationState.LoadingApi);
            SetMessage("Завантаження даних з сервера...");
            var apiResult = await _apiClient.GetReleasesAsync();
            if (apiResult.IsSuccess && apiResult.Value?.Data?.Modes != null)
            {
                _apiResponse = apiResult.Value;
                _apiLoadedSuccessfully = true;
                _apiErrorMessage = null;
            }
            else
            {
                _apiLoadedSuccessfully = false;
                _apiErrorMessage = apiResult.ErrorMessage ?? "Невідома помилка сервера.";
            }

            // 3. Game detection
            SetOperationState(OperationState.DetectingGame);
            var patterns = _apiResponse?.Data?.InstallPathPatterns;
            var detection = await _gameDetector.DetectAsync(patterns);
            if (detection.IsFound && detection.GamePath != null)
            {
                _gameRoot = detection.GamePath;
                SetGamePathText(detection.GamePath);
            }
            else
            {
                SetGamePathText("Гру не знайдено");
            }

            // 4. State + compatibility refresh
            await RefreshStateAsync();
        }
        catch (Exception ex)
        {
            _logger.Error($"Startup error: {ex.Message}");
            SetMessage($"Помилка запуску: {ex.Message}");
        }
        finally
        {
            _initializing = false;
            SetOperationState(OperationState.Idle);
        }
    }

    private void RestoreLastMode(Config config)
    {
        if (config.LastMode != null && KnownModeSlugs.Contains(config.LastMode))
        {
            SelectModeBySlug(config.LastMode);
        }
    }

    private void SelectModeBySlug(string slug)
    {
        if (slug == (string)bosiaRadioButton.Tag!) bosiaRadioButton.Checked = true;
        else if (slug == (string)englishItemsRadioButton.Tag!) englishItemsRadioButton.Checked = true;
        else fullUkrainianRadioButton.Checked = true;
    }

    // --- Detect button ---

    private async void DetectGameButton_Click(object? sender, EventArgs e)
    {
        detectGameButton.Enabled = false;
        SetOperationState(OperationState.DetectingGame);
        SetMessage("Пошук гри...");
        try
        {
            var patterns = _apiResponse?.Data?.InstallPathPatterns;
            var result = await _gameDetector.DetectAsync(patterns);
            if (result.IsFound && result.GamePath != null)
            {
                _gameRoot = result.GamePath;
                SetGamePathText(result.GamePath);
                await RefreshStateAsync();
            }
            else
            {
                _gameRoot = null;
                SetGamePathText("Гру не знайдено");
                await RefreshStateAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"Detection error: {ex.Message}");
            SetMessage($"Помилка пошуку: {ex.Message}");
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
                Description = "Оберіть папку гри Black Desert Online"
            };

            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;

            var result = await _gameDetector.ValidateAndSaveManualPathAsync(dialog.SelectedPath);
            if (result.IsFound && result.GamePath != null)
            {
                _gameRoot = result.GamePath;
                SetGamePathText(result.GamePath);
                await RefreshStateAsync();
            }
            else
            {
                SetMessage("Обрана папка не містить гри Black Desert Online.");
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"Browse error: {ex.Message}");
            SetMessage($"Помилка вибору папки: {ex.Message}");
        }
    }

    // --- Mode change ---

    private async void ModeRadioButton_CheckedChanged(object? sender, EventArgs e)
    {
        if (_initializing) return;
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

    // --- Install / Update action ---

    private async void InstallButton_Click(object? sender, EventArgs e)
    {
        try
        {
            await HandleInstallOrUpdateAsync(isUpdate: false);
        }
        catch (Exception ex)
        {
            _logger.Error($"InstallButton_Click unexpected: {ex.Message}");
            SetMessage($"Помилка: {ex.Message}");
        }
    }

    private async void UpdateButton_Click(object? sender, EventArgs e)
    {
        try
        {
            await HandleInstallOrUpdateAsync(isUpdate: true);
        }
        catch (Exception ex)
        {
            _logger.Error($"UpdateButton_Click unexpected: {ex.Message}");
            SetMessage($"Помилка: {ex.Message}");
        }
    }

    private async Task HandleInstallOrUpdateAsync(bool isUpdate)
    {
        if (_operationInProgress) return;

        string? finalMessage = null;

        try
        {
            _operationInProgress = true;
            SetOperationState(OperationState.Idle);
            SetActionsEnabled(false, false, false, false);
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

            // Factual state precondition
            var expectedState = isUpdate
                ? LocalizationState.UpdateAvailable
                : LocalizationState.NotInstalled;

            if (_lastResolvedState != expectedState)
            {
                await RefreshStateAsync();

                if (_lastResolvedState != expectedState)
                {
                    finalMessage = isUpdate
                        ? "Оновлення недоступне для поточного стану локалізації."
                        : "Встановлення недоступне для поточного стану локалізації.";
                    return;
                }
            }

            var actionLabel = isUpdate ? "Оновлення" : "Встановлення";
            SetMessage($"{actionLabel}...");
            SetProgress(0);
            SetOperationState(OperationState.Downloading);

            _operationCts = new CancellationTokenSource();
            cancelButton.Enabled = true;

            var service = new LocalizationInstallService(
                _localizationInstaller, _backupStore, _stateStore, _logger, _gameRoot);

            var progress = new Progress<DownloadProgress>(OnDownloadProgress);

            var result = await service.InstallReleaseAsync(
                GetSelectedModeSlug(), current, progress, _operationCts.Token);

            if (result.IsSuccess)
            {
                SetOperationState(OperationState.Completed);
                finalMessage = isUpdate
                    ? "Локалізацію успішно оновлено."
                    : "Локалізацію успішно встановлено.";
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
            _logger.Info("Install/update cancelled by user.");
            SetOperationState(OperationState.Cancelled);
            finalMessage = isUpdate
                ? "Оновлення скасовано."
                : "Встановлення скасовано.";
        }
        catch (Exception ex)
        {
            _logger.Error($"Install/update error: {ex.Message}");
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
            SetOperationState(OperationState.Idle);
            SetActionsEnabled(false, false, false, false);
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
            return;

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
        fullUkrainianRadioButton.Enabled = enabled;
        bosiaRadioButton.Enabled = enabled;
        englishItemsRadioButton.Enabled = enabled;
    }

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
        SetActionsEnabled(false, false, false, false);

        if (_gameRoot == null)
        {
            SetLocalizationStateText("Не визначено");
            SetDetailsText("");
            _lastResolvedState = LocalizationState.NotInstalled;
            if (!_apiLoadedSuccessfully)
                SetMessage($"Помилка завантаження API: {_apiErrorMessage}");
            else
                SetMessage("Гру не знайдено. Натисніть \"Знайти гру\" або оберіть папку вручну.");
            return;
        }

        if (!_apiLoadedSuccessfully)
        {
            SetLocalizationStateText("Не визначено");
            SetDetailsText("");
            _lastResolvedState = LocalizationState.NotInstalled;
            SetMessage($"Помилка завантаження API: {_apiErrorMessage}");
            return;
        }

        var mode = GetSelectedApiMode();

        if (mode == null)
        {
            SetLocalizationStateText("Не визначено");
            SetDetailsText("");
            _lastResolvedState = LocalizationState.NotInstalled;
            SetMessage($"Режим \"{GetSelectedModeSlug()}\" не знайдено на сервері.");
            return;
        }

        var current = mode.Current;

        // Details
        if (current != null)
        {
            SetDetailsText($"{mode.PublicName ?? GetSelectedModeSlug()} | v{current.Version} | patch {current.Patch}");
        }
        else
        {
            SetDetailsText($"{mode.PublicName ?? GetSelectedModeSlug()} | реліз ще не опубліковано");
        }

        // Localization state
        var gameLocPath = Path.Combine(_gameRoot, "ads", "languagedata_en.loc");
        var stateResult = await _stateService.ResolveAsync(current, gameLocPath);
        _lastResolvedState = stateResult.State;
        SetLocalizationStateText(GetStateDisplayText(stateResult.State));

        // Diagnostics priority: state error > compatibility reason > Corrupted fallback
        string? diagnostic = stateResult.Error;

        var compatResult = _compatService.Check(current);
        if (diagnostic == null && !compatResult.IsAllowed && compatResult.Reason != null)
            diagnostic = compatResult.Reason;

        if (diagnostic == null && stateResult.State == LocalizationState.Corrupted)
            diagnostic = "Файл локалізації пошкоджено. Спробуйте встановити знову.";

        SetMessage(diagnostic ?? "");

        // Action availability
        var canInstall = !_operationInProgress
            && current != null
            && compatResult.IsAllowed
            && stateResult.State == LocalizationState.NotInstalled;

        var canUpdate = !_operationInProgress
            && current != null
            && compatResult.IsAllowed
            && stateResult.State == LocalizationState.UpdateAvailable;

        var canRestoreOriginal = !_operationInProgress
            && stateResult.State is LocalizationState.UpToDate
                or LocalizationState.UpdateAvailable
                or LocalizationState.WaitingForRelease
                or LocalizationState.Corrupted
                or LocalizationState.InstalledVersionUnknown;

        SetActionsEnabled(canInstall, canUpdate, canRestoreOriginal, false);
    }

    private static string GetStateDisplayText(LocalizationState state) => state switch
    {
        LocalizationState.NotInstalled => "Не встановлено",
        LocalizationState.UpToDate => "Актуальна",
        LocalizationState.UpdateAvailable => "Доступне оновлення",
        LocalizationState.WaitingForRelease => "Очікується реліз",
        LocalizationState.InstalledVersionUnknown => "Версію не вдалося визначити",
        LocalizationState.Corrupted => "Файл локалізації пошкоджено",
        _ => "Не визначено"
    };

    // --- Presentation helpers ---

    public void SetGamePathText(string text) => gamePathLabel.Text = text;

    public void SetLocalizationStateText(string text) => localizationStateLabel.Text = text;

    public void SetDetailsText(string text) => detailsLabel.Text = text;

    public void SetProgress(int percent)
    {
        progressBar.Value = Math.Clamp(percent, 0, 100);
        progressLabel.Text = $"{progressBar.Value}%";
    }

    public void SetMessage(string text) => messageTextBox.Text = text;

    public string GetSelectedModeSlug()
    {
        if (bosiaRadioButton.Checked) return (string)bosiaRadioButton.Tag!;
        if (englishItemsRadioButton.Checked) return (string)englishItemsRadioButton.Tag!;
        return (string)fullUkrainianRadioButton.Tag!;
    }

    private LocalizationMode? GetSelectedApiMode()
    {
        if (_apiResponse?.Data?.Modes == null) return null;
        var slug = GetSelectedModeSlug();
        return _apiResponse.Data.Modes
            .FirstOrDefault(m => string.Equals(m.Slug, slug, StringComparison.Ordinal));
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
        RestoreError.BackupIo => "Помилка запису резервної копії.",
        RestoreError.OfficialDownloadFailed => "Не вдалося завантажити оригінальний файл з сервера.",
        RestoreError.FallbackNotAllowed => "Відновлення з локальної копії неможливе (патч не збігається або копія відсутня).",
        RestoreError.PatchMismatch => "Патч локальної копії не збігається з поточним офіційним патчем.",
        RestoreError.ReplaceFailed => "Не вдалося замінити файл локалізації у папці гри.",
        RestoreError.VerificationFailed => "Перевірка відновленого файлу не пройдена.",
        RestoreError.StateSaveFailed => "Не вдалося зберегти стан встановлення після відновлення.",
        RestoreError.RecoveryFailed => "Не вдалося відновити попередній стан. Перевірте файли гри та журнал.",
        _ => "Невідома помилка відновлення."
    };

    public void SetActionsEnabled(bool install, bool update, bool restoreOriginal, bool restoreBackup)
    {
        installButton.Enabled = install;
        updateButton.Enabled = update;
        restoreOriginalButton.Enabled = restoreOriginal;
        restoreBackupButton.Enabled = restoreBackup;
    }
}
