#nullable enable
namespace BdoClient;

partial class MainForm
{
    private System.ComponentModel.IContainer? components = null;

    // --- Game Detection ---
    private GroupBox gameGroupBox = null!;
    private Label gameStatusLabel = null!;
    private Label gamePathLabel = null!;
    private Button detectGameButton = null!;
    private Button browseGameButton = null!;

    // --- Localization Mode ---
    private GroupBox modeGroupBox = null!;
    private FlowLayoutPanel modesFlowPanel = null!;

    // --- Status ---
    private GroupBox statusGroupBox = null!;
    private Label localizationStateLabel = null!;
    private Label installedInfoLabel = null!;
    private Label detailsLabel = null!;
    private ProgressBar progressBar = null!;
    private Label progressLabel = null!;
    private TextBox messageTextBox = null!;

    // --- Footer ---
    private TableLayoutPanel footerPanel = null!;
    private FlowLayoutPanel leftActionsPanel = null!;
    private Button installButton = null!;
    private Button restoreOriginalButton = null!;
    private Button cancelButton = null!;
    private FlowLayoutPanel rightUtilityPanel = null!;
    private Button updateButton = null!;
    private Label versionLabel = null!;
    private Button logsButton = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null))
        {
            components.Dispose();
        }
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        this.SuspendLayout();

        // --- MainForm ---
        this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
        this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
        this.ClientSize = new System.Drawing.Size(640, 560);
        this.MinimumSize = new System.Drawing.Size(620, 480);
        this.Name = "MainForm";
        this.Text = "BDO UA Client";
        this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;

        // --- Main layout ---
        var mainLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(8)
        };
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // Game
        mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // Mode
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F)); // Status
        mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // Actions

        // ==========================================
        // Game Detection Block
        // ==========================================
        gameGroupBox = new GroupBox
        {
            Text = "Гра",
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(8, 4, 8, 8)
        };

        var gameLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            AutoSize = true
        };
        gameLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        gameLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        gameLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        gameLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        gameStatusLabel = new Label
        {
            Text = "Гра ще не перевірена",
            Dock = DockStyle.Fill,
            AutoSize = true,
            ForeColor = System.Drawing.SystemColors.GrayText,
            Margin = new Padding(0, 0, 0, 2)
        };

        gamePathLabel = new Label
        {
            Text = "",
            Dock = DockStyle.Fill,
            AutoSize = false,
            AutoEllipsis = true,
            TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
            Margin = new Padding(0, 0, 8, 0)
        };

        var gameButtonsPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };

        detectGameButton = new Button
        {
            Text = "Знайти гру",
            AutoSize = true,
            Margin = new Padding(0, 0, 4, 0)
        };

        browseGameButton = new Button
        {
            Text = "Обрати вручну",
            AutoSize = true,
            Margin = new Padding(0)
        };

        gameButtonsPanel.Controls.AddRange(new Control[] { detectGameButton, browseGameButton });
        gameLayout.Controls.Add(gameStatusLabel, 0, 0);
        gameLayout.Controls.Add(gamePathLabel, 0, 1);
        gameLayout.Controls.Add(gameButtonsPanel, 1, 1);
        gameGroupBox.Controls.Add(gameLayout);

        // ==========================================
        // Localization Mode Block
        // ==========================================
        modeGroupBox = new GroupBox
        {
            Text = "Режим локалізації",
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(8, 4, 8, 8)
        };

        modesFlowPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false
        };

        modeGroupBox.Controls.Add(modesFlowPanel);

        // ==========================================
        // Status / Progress Block
        // ==========================================
        statusGroupBox = new GroupBox
        {
            Text = "Стан",
            Dock = DockStyle.Fill,
            AutoSize = false,
            Padding = new Padding(8, 4, 8, 8)
        };

        var statusLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6,
            AutoSize = false
        };
        statusLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        statusLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // state
        statusLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // installed info
        statusLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // details
        statusLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 8F)); // spacer
        statusLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // progress row
        statusLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F)); // message

        localizationStateLabel = new Label
        {
            Text = "Не визначено",
            Dock = DockStyle.Top,
            AutoSize = true,
            Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Bold),
            ForeColor = SystemColors.ControlText,
            Margin = new Padding(0, 0, 0, 2)
        };

        installedInfoLabel = new Label
        {
            Text = "",
            Dock = DockStyle.Top,
            AutoSize = true,
            Font = new System.Drawing.Font("Segoe UI", 9.5F),
            ForeColor = System.Drawing.SystemColors.ControlText,
            Margin = new Padding(0, 0, 0, 2)
        };

        detailsLabel = new Label
        {
            Text = "",
            Dock = DockStyle.Top,
            AutoSize = true,
            Font = new System.Drawing.Font("Segoe UI", 9.5F),
            ForeColor = System.Drawing.SystemColors.ControlText,
            Margin = new Padding(0, 0, 0, 4)
        };

        var progressPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            RowCount = 1,
            AutoSize = true
        };
        progressPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        progressPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        progressBar = new ProgressBar
        {
            Dock = DockStyle.Fill,
            Minimum = 0,
            Maximum = 100,
            Value = 0,
            Height = 20,
            Margin = new Padding(0, 0, 8, 0)
        };

        progressLabel = new Label
        {
            Text = "0%",
            AutoSize = true,
            TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
            Margin = new Padding(0)
        };

        progressPanel.Controls.Add(progressBar, 0, 0);
        progressPanel.Controls.Add(progressLabel, 1, 0);

        messageTextBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            BackColor = System.Drawing.SystemColors.Control,
            BorderStyle = BorderStyle.None,
            ScrollBars = ScrollBars.Vertical,
            Margin = new Padding(0, 4, 0, 0)
        };

        statusLayout.Controls.Add(localizationStateLabel, 0, 0);
        statusLayout.Controls.Add(installedInfoLabel, 0, 1);
        statusLayout.Controls.Add(detailsLabel, 0, 2);
        statusLayout.Controls.Add(progressPanel, 0, 4);
        statusLayout.Controls.Add(messageTextBox, 0, 5);
        statusGroupBox.Controls.Add(statusLayout);

        // ==========================================
        // Footer (actions + utility)
        // ==========================================
        footerPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Bottom,
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(0, 8, 0, 0)
        };
        footerPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        footerPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footerPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        leftActionsPanel = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Anchor = AnchorStyles.Left | AnchorStyles.Top,
            Margin = new Padding(0)
        };

        installButton = new Button
        {
            Text = "Встановити",
            AutoSize = true,
            Enabled = false,
            Margin = new Padding(0, 0, 8, 0)
        };

        restoreOriginalButton = new Button
        {
            Text = "Відновити оригінал",
            AutoSize = true,
            Enabled = false,
            Margin = new Padding(0, 0, 8, 0)
        };

        cancelButton = new Button
        {
            Text = "Скасувати",
            AutoSize = true,
            Enabled = false,
            Margin = new Padding(0)
        };

        leftActionsPanel.Controls.AddRange(new Control[]
        {
            installButton, restoreOriginalButton, cancelButton
        });

        rightUtilityPanel = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Anchor = AnchorStyles.Right | AnchorStyles.Top,
            Margin = new Padding(0)
        };

        updateButton = new Button
        {
            Text = "Оновити до vX.Y.Z",
            AutoSize = true,
            Visible = false,
            Margin = new Padding(0, 0, 8, 0)
        };

        versionLabel = new Label
        {
            Text = "",
            AutoSize = true,
            TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
            Font = new System.Drawing.Font("Segoe UI", 9F),
            ForeColor = System.Drawing.SystemColors.GrayText,
            Margin = new Padding(0, 4, 8, 0)
        };

        logsButton = new Button
        {
            Text = "\U0001F4C1",
            AutoSize = false,
            Size = new System.Drawing.Size(28, 28),
            FlatStyle = FlatStyle.Flat,
            Margin = new Padding(0),
            AccessibleName = "Відкрити папку журналів"
        };
        logsButton.FlatAppearance.BorderSize = 0;

        var logsToolTip = new ToolTip();
        logsToolTip.SetToolTip(logsButton, "Відкрити папку журналів");

        rightUtilityPanel.Controls.AddRange(new Control[]
        {
            updateButton, versionLabel, logsButton
        });

        footerPanel.Controls.Add(leftActionsPanel, 0, 0);
        footerPanel.Controls.Add(rightUtilityPanel, 1, 0);

        // ==========================================
        // Assemble main layout
        // ==========================================
        mainLayout.Controls.Add(gameGroupBox, 0, 0);
        mainLayout.Controls.Add(modeGroupBox, 0, 1);
        mainLayout.Controls.Add(statusGroupBox, 0, 2);
        mainLayout.Controls.Add(footerPanel, 0, 3);

        this.Controls.Add(mainLayout);
        this.ResumeLayout(false);
    }
}
