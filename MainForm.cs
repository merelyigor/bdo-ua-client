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
    private readonly ILogger _logger;

    private string? _gameRoot;
    private ReleasesResponse? _apiResponse;
    private bool _apiLoadedSuccessfully;
    private string? _apiErrorMessage;
    private bool _initializing;

    private static readonly string[] KnownModeSlugs = new[]
    {
        "full-ukrainian", "full-ukrainian-bosia", "english-items"
    };

    public MainForm(
        AppPaths appPaths,
        ConfigStore configStore,
        InstallationStateStore stateStore,
        BdoUaApiClient apiClient,
        GameDetector gameDetector,
        LocalizationStateService stateService,
        LocalizationCompatibilityService compatService,
        ILogger logger)
    {
        _configStore = configStore;
        _apiClient = apiClient;
        _gameDetector = gameDetector;
        _stateService = stateService;
        _compatService = compatService;
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

        if (configWarning != null && string.IsNullOrEmpty(messageTextBox.Text))
            SetMessage(configWarning);
    }

    // --- State refresh ---

    private async Task RefreshStateAsync()
    {
        SetActionsEnabled(false, false, false, false);

        if (_gameRoot == null)
        {
            SetLocalizationStateText("Не визначено");
            SetDetailsText("");
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
            SetMessage($"Помилка завантаження API: {_apiErrorMessage}");
            return;
        }

        var selectedSlug = GetSelectedModeSlug();
        var mode = _apiResponse!.Data!.Modes?
            .FirstOrDefault(m => string.Equals(m.Slug, selectedSlug, StringComparison.Ordinal));

        if (mode == null)
        {
            SetLocalizationStateText("Не визначено");
            SetDetailsText("");
            SetMessage($"Режим \"{selectedSlug}\" не знайдено на сервері.");
            return;
        }

        var current = mode.Current;

        // Details
        if (current != null)
        {
            SetDetailsText($"{mode.PublicName ?? selectedSlug} | v{current.Version} | patch {current.Patch}");
        }
        else
        {
            SetDetailsText($"{mode.PublicName ?? selectedSlug} | реліз ще не опубліковано");
        }

        // Localization state
        var gameLocPath = Path.Combine(_gameRoot, "ads", "languagedata_en.loc");
        var stateResult = await _stateService.ResolveAsync(current, gameLocPath);
        SetLocalizationStateText(GetStateDisplayText(stateResult.State));

        // Diagnostics priority: state error > compatibility reason > Corrupted fallback
        string? diagnostic = stateResult.Error;

        var compatResult = _compatService.Check(current);
        if (diagnostic == null && !compatResult.IsAllowed && compatResult.Reason != null)
            diagnostic = compatResult.Reason;

        if (diagnostic == null && stateResult.State == LocalizationState.Corrupted)
            diagnostic = "Файл локалізації пошкоджено. Спробуйте встановити знову.";

        SetMessage(diagnostic ?? "");

        // Action availability (computed but buttons stay disabled in v9.0)
        // Install: NotInstalled + compatible + current exists
        // Update: UpdateAvailable + compatible + current exists
        // These will be enabled in v9.1 when click handlers are wired.
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

    public void SetActionsEnabled(bool install, bool update, bool restoreOriginal, bool restoreBackup)
    {
        installButton.Enabled = install;
        updateButton.Enabled = update;
        restoreOriginalButton.Enabled = restoreOriginal;
        restoreBackupButton.Enabled = restoreBackup;
    }
}
