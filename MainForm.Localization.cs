using System.Drawing;
using System.Windows.Forms;
using BdoClient.Api;
using BdoClient.Models;
using BdoClient.Services;
using BdoClient.Storage;

namespace BdoClient;

public partial class MainForm
{

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


    private async Task RefreshStateAsync()
    {
        SetActionsEnabled(false);

        if (_gameRoot == null)
        {
            _lastResolvedState = LocalizationState.NotInstalled;
            ClearLocalFileTracking();
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
        LocalizationFileFingerprint.TryCapture(gameLocPath, out var capturedFingerprint, out var captureError);
        bool fingerprintCaptured = captureError == null;
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

        if (fingerprintCaptured
            && installedLoad.Status == FileLoadStatus.Valid
            && installedLoad.Value?.Source == InstallationSource.Api)
        {
            _localFileChangeTracker.CommitResolved(gameLocPath, capturedFingerprint);
        }

        StartLocalFileMonitorIfEligible();
        ObserveLocalizationNotification(stateResult.State);
    }
}
