using BdoClient.Models;

namespace BdoClient;

public class RestorePointSelectionForm : Form
{
    private readonly ListView _listView;
    private readonly Button _restoreButton;
    private readonly Button _cancelButton;

    public RestorePointInfo? SelectedRestorePoint { get; private set; }

    public RestorePointSelectionForm(IReadOnlyList<RestorePointInfo> restorePoints)
    {
        Text = "Відновлення резервної копії";
        Size = new System.Drawing.Size(650, 380);
        MinimumSize = new System.Drawing.Size(550, 300);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        ShowInTaskbar = false;

        var mainLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(8)
        };
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _listView = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            GridLines = true,
            MultiSelect = false
        };
        _listView.Columns.Add("Дата", 160);
        _listView.Columns.Add("Патч", 60);
        _listView.Columns.Add("Операція", 220);
        _listView.Columns.Add("Розмір", 80);
        _listView.SelectedIndexChanged += (_, _) => _restoreButton!.Enabled = _listView.SelectedItems.Count > 0;

        foreach (var point in restorePoints)
        {
            var item = new ListViewItem(point.CreatedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss"));
            item.SubItems.Add(point.GamePatch?.ToString() ?? "невідомо");
            item.SubItems.Add(MapSource(point.Source));
            item.SubItems.Add(FormatSize(point.SizeBytes));
            item.Tag = point;
            _listView.Items.Add(item);
        }

        var buttonsPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(0, 4, 0, 0)
        };

        _cancelButton = new Button
        {
            Text = "Скасувати",
            AutoSize = true,
            DialogResult = DialogResult.Cancel
        };

        _restoreButton = new Button
        {
            Text = "Відновити",
            AutoSize = true,
            Enabled = false,
            Margin = new Padding(0, 0, 8, 0)
        };
        _restoreButton.Click += RestoreButton_Click;

        buttonsPanel.Controls.Add(_cancelButton);
        buttonsPanel.Controls.Add(_restoreButton);

        mainLayout.Controls.Add(_listView, 0, 0);
        mainLayout.Controls.Add(buttonsPanel, 0, 1);
        Controls.Add(mainLayout);

        AcceptButton = _restoreButton;
        CancelButton = _cancelButton;
    }

    private void RestoreButton_Click(object? sender, EventArgs e)
    {
        if (_listView.SelectedItems.Count == 0) return;
        SelectedRestorePoint = _listView.SelectedItems[0].Tag as RestorePointInfo;
        if (SelectedRestorePoint == null) return;
        DialogResult = DialogResult.OK;
        Close();
    }

    private static string MapSource(string? source) => source switch
    {
        "pre_install" => "Перед встановленням/оновленням",
        "restore_original" => "Перед відновленням оригіналу",
        "restore_original_fallback" => "Перед відновленням оригіналу",
        "pre_restore_backup" => "Перед відновленням копії",
        _ => "Резервна копія"
    };

    private static string FormatSize(long bytes)
    {
        double mb = bytes / (1024.0 * 1024.0);
        return mb < 0.01 ? "< 0.01 MB" : $"{mb:F2} MB";
    }
}
