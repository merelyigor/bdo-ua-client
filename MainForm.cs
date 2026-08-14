using System.Windows.Forms;

namespace BdoClient;

public partial class MainForm : Form
{
    public MainForm()
    {
        InitializeComponent();
    }

    // --- Presentation-only helper methods for Stage 9 wiring ---

    public void SetGamePathText(string text)
    {
        gamePathLabel.Text = text;
    }

    public void SetLocalizationStateText(string text)
    {
        localizationStateLabel.Text = text;
    }

    public void SetDetailsText(string text)
    {
        detailsLabel.Text = text;
    }

    public void SetProgress(int percent)
    {
        progressBar.Value = Math.Clamp(percent, 0, 100);
        progressLabel.Text = $"{progressBar.Value}%";
    }

    public void SetMessage(string text)
    {
        messageTextBox.Text = text;
    }

    public string GetSelectedModeSlug()
    {
        if (fullUkrainianRadioButton.Checked) return (string)fullUkrainianRadioButton.Tag!;
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

    // --- Event handlers (empty — Stage 9 will wire real logic) ---

    private void DetectGameButton_Click(object? sender, EventArgs e) { }

    private void BrowseGameButton_Click(object? sender, EventArgs e) { }

    private void InstallButton_Click(object? sender, EventArgs e) { }

    private void UpdateButton_Click(object? sender, EventArgs e) { }

    private void RestoreOriginalButton_Click(object? sender, EventArgs e) { }

    private void RestoreBackupButton_Click(object? sender, EventArgs e) { }
}
