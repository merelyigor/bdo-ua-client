using System.Drawing;
using System.Windows.Forms;

namespace BdoClient;

internal sealed class UpdateApplyingForm : Form
{
    private readonly Func<Task<int>> _applyUpdate;
    private bool _completed;

    public int ExitCode { get; private set; } = BdoClient.Update.SelfUpdateApplier.ExitCodeReplaceFailed;

    public UpdateApplyingForm(Func<Task<int>> applyUpdate)
    {
        _applyUpdate = applyUpdate ?? throw new ArgumentNullException(nameof(applyUpdate));

        Text = "BDO UA Client — оновлення";
        ClientSize = new Size(480, 178);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        TopMost = true;
        MaximizeBox = false;
        MinimizeBox = false;
        ControlBox = false;
        ShowInTaskbar = true;
        BackColor = UiTheme.Background;
        ForeColor = UiTheme.PrimaryText;
        Font = new Font("Segoe UI", 9F);

        var mainText = new Label
        {
            AutoSize = true,
            Location = new Point(24, 18),
            Text = "Застосування оновлення...",
            ForeColor = UiTheme.PrimaryText,
            Font = new Font("Segoe UI", 12F, FontStyle.Bold)
        };
        var secondaryText = new Label
        {
            AutoSize = false,
            Location = new Point(24, 52),
            Size = new Size(432, 48),
            ForeColor = UiTheme.SecondaryText,
            Text = "Будь ласка, зачекайте.\nПрограма запуститься автоматично після завершення оновлення."
        };
        var progressBar = new BdoProgressBar
        {
            Location = new Point(24, 124),
            Size = new Size(432, 20),
            BackColor = UiTheme.ControlBackground,
            ForeColor = UiTheme.Accent,
            Style = ProgressBarStyle.Marquee,
            MarqueeAnimationSpeed = 30
        };

        Controls.Add(mainText);
        Controls.Add(secondaryText);
        Controls.Add(progressBar);
        Shown += StartUpdateAsync;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_completed)
            e.Cancel = true;
        base.OnFormClosing(e);
    }

    private async void StartUpdateAsync(object? sender, EventArgs e)
    {
        try
        {
            ExitCode = await Task.Run(_applyUpdate);
        }
        finally
        {
            _completed = true;
            Close();
        }
    }
}
