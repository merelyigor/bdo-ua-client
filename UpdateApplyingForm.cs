using System.Drawing;

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
        ClientSize = new Size(460, 150);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        ControlBox = false;
        ShowInTaskbar = true;

        var mainText = new Label
        {
            AutoSize = true,
            Location = new Point(24, 20),
            Text = "Застосування оновлення...",
            Font = new Font(SystemFonts.MessageBoxFont ?? SystemFonts.DefaultFont, FontStyle.Bold)
        };
        var secondaryText = new Label
        {
            AutoSize = false,
            Location = new Point(24, 52),
            Size = new Size(412, 38),
            Text = "Будь ласка, зачекайте.\nПрограма запуститься автоматично після завершення оновлення."
        };
        var progressBar = new ProgressBar
        {
            Location = new Point(24, 108),
            Size = new Size(412, 18),
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
