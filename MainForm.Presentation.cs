using System.Drawing;
using System.Windows.Forms;
using BdoClient.Models;
using BdoClient.Services;

namespace BdoClient;

public partial class MainForm
{
    // --- Theme / shell layout ---

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

    private void ModesFlowPanel_Resize(object? sender, EventArgs e)
    {
        RefreshModeCardLayout();
        ScheduleContentFit();
    }

    // --- Game-status presentation ---

    private void SetGameFound(string path, DetectionSource? source)
    {
        var status = source == DetectionSource.Manual
            ? "✓ Гру знайдено вручну"
            : "✓ Гру знайдено";
        var patch = AdsFilesPatchReader.TryReadPatch(path);
        gameStatusLabel.Text = patch is > 0
            ? $"{status} • patch {patch.Value}"
            : status;
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

    // --- Operation/control presentation ---

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

    // --- Mode-card presentation ---

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

    // --- Public presentation helpers ---

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

    // --- Designer-referenced visual helper ---

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
