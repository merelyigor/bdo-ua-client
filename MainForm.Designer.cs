#nullable enable
namespace BdoClient;

partial class MainForm
{
    private System.ComponentModel.IContainer? components = null;
    private Panel rootScrollPanel = null!;
    private TableLayoutPanel mainLayoutPanel = null!;

    private Panel headerPanel = null!;
    private Label headerTitleLabel = null!;
    private Label headerSubtitleLabel = null!;
    private Panel headerAccentLine = null!;

    private Panel gameGroupBox = null!;
    private Label gameSectionCaptionLabel = null!;
    private Label gameStatusLabel = null!;
    private Label gamePathLabel = null!;
    private Button detectGameButton = null!;
    private Button browseGameButton = null!;

    private Panel modeGroupBox = null!;
    private Label modeSectionCaptionLabel = null!;
    private FlowLayoutPanel modesFlowPanel = null!;

    private Panel statusGroupBox = null!;
    private Label statusSectionCaptionLabel = null!;
    private Label localizationStateLabel = null!;
    private Label installedCaptionLabel = null!;
    private Label installedModeLabel = null!;
    private Label installedMetaLabel = null!;
    private Label targetCaptionLabel = null!;
    private Label targetModeLabel = null!;
    private Label targetMetaLabel = null!;
    private Label installedInfoLabel = null!;
    private Label detailsLabel = null!;
    private BdoProgressBar progressBar = null!;
    private Label progressLabel = null!;
    private TextBox messageTextBox = null!;

    private TableLayoutPanel footerPanel = null!;
    private FlowLayoutPanel leftActionsPanel = null!;
    private FlowLayoutPanel rightActionsPanel = null!;
    private Button installButton = null!;
    private Button restoreOriginalButton = null!;
    private Button cancelButton = null!;
    private FlowLayoutPanel rightUtilityPanel = null!;
    private Button updateButton = null!;
    private Label versionLabel = null!;
    private Button logsButton = null!;
    private ToolTip logsToolTip = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing && components != null)
            components.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        SuspendLayout();

        AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new System.Drawing.Size(800, 650);
        MinimumSize = new System.Drawing.Size(700, 500);
        Name = "MainForm";
        StartPosition = FormStartPosition.CenterScreen;
        Text = "BDO UA Client";

        mainLayoutPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right,
            ColumnCount = 1,
            RowCount = 5,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(16),
            BackColor = UiTheme.Background
        };
        mainLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        mainLayoutPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        mainLayoutPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        mainLayoutPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        mainLayoutPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        mainLayoutPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        headerPanel = new Panel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            BackColor = UiTheme.Background,
            Margin = new Padding(0, 0, 0, 12)
        };
        var headerLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 3,
            AutoSize = true,
            BackColor = UiTheme.Background,
            Margin = new Padding(0)
        };
        headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        headerLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        headerLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        headerLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        headerTitleLabel = new Label
        {
            Text = "BDO UA Client",
            AutoSize = true,
            Dock = DockStyle.Fill,
            Font = new System.Drawing.Font("Segoe UI", 16F, FontStyle.Bold),
            ForeColor = UiTheme.PrimaryText,
            Margin = new Padding(0)
        };
        headerSubtitleLabel = new Label
        {
            Text = "Українська локалізація Black Desert Online",
            AutoSize = true,
            Dock = DockStyle.Fill,
            Font = new System.Drawing.Font("Segoe UI", 9.5F),
            ForeColor = UiTheme.SecondaryText,
            Margin = new Padding(0, 2, 0, 0)
        };
        headerAccentLine = new Panel
        {
            Dock = DockStyle.Fill,
            Height = 2,
            BackColor = UiTheme.Accent,
            Margin = new Padding(0, 6, 0, 0)
        };
        headerLayout.Controls.Add(headerTitleLabel, 0, 0);
        headerLayout.Controls.Add(headerSubtitleLabel, 0, 1);
        headerLayout.Controls.Add(headerAccentLine, 0, 2);
        headerPanel.Controls.Add(headerLayout);

        rightUtilityPanel = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = UiTheme.Background,
            Margin = new Padding(0, 8, 0, 0),
            Padding = new Padding(0)
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
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new System.Drawing.Font("Segoe UI", 8.5F),
            ForeColor = UiTheme.SecondaryText,
            Margin = new Padding(0, 6, 8, 0)
        };
        logsButton = new Button
        {
            Text = "",
            AutoSize = false,
            Size = new System.Drawing.Size(28, 28),
            FlatStyle = FlatStyle.Flat,
            Margin = new Padding(0),
            AccessibleName = "Відкрити папку журналів",
            Image = BuildLogsIcon(),
            ImageAlign = ContentAlignment.MiddleCenter
        };
        logsButton.FlatAppearance.BorderSize = 0;
        rightUtilityPanel.Controls.AddRange(new Control[] { updateButton, versionLabel, logsButton });
        headerLayout.Controls.Add(rightUtilityPanel, 1, 0);
        headerLayout.SetRowSpan(rightUtilityPanel, 3);

        logsToolTip = new ToolTip();
        logsToolTip.SetToolTip(logsButton, "Відкрити папку журналів");
        components ??= new System.ComponentModel.Container();
        components.Add(logsToolTip);

        gameGroupBox = CreateCard();
        gameSectionCaptionLabel = CreateSectionCaption("ГРА");
        gameStatusLabel = new Label
        {
            Text = "Гра ще не перевірена",
            AutoSize = true,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 4)
        };
        gamePathLabel = new Label
        {
            Text = "",
            AutoSize = false,
            Dock = DockStyle.Fill,
            AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, 0, 12, 0)
        };
        detectGameButton = new Button
        {
            Text = "Знайти автоматично",
            AutoSize = true,
            Margin = new Padding(0, 0, 8, 0)
        };
        browseGameButton = new Button
        {
            Text = "Обрати папку",
            AutoSize = true,
            Margin = new Padding(0)
        };
        var gameButtonsPanel = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0)
        };
        gameButtonsPanel.Controls.AddRange(new Control[] { detectGameButton, browseGameButton });
        var gameLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            AutoSize = true,
            Margin = new Padding(0)
        };
        gameLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        gameLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        gameLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        gameLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        gameLayout.Controls.Add(gameStatusLabel, 0, 0);
        gameLayout.SetColumnSpan(gameStatusLabel, 2);
        gameLayout.Controls.Add(gamePathLabel, 0, 1);
        gameLayout.Controls.Add(gameButtonsPanel, 1, 1);
        gameGroupBox.Controls.Add(gameLayout);
        gameGroupBox.Controls.Add(gameSectionCaptionLabel);

        modeGroupBox = CreateCard();
        modeGroupBox.Padding = new Padding(16);
        modeSectionCaptionLabel = CreateSectionCaption("ЛОКАЛІЗАЦІЯ");
        modeSectionCaptionLabel.Margin = new Padding(0);
        modesFlowPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            AutoScroll = false,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
            Padding = new Padding(0, 12, 0, 0)
        };
        modeGroupBox.Controls.Add(modesFlowPanel);
        modeGroupBox.Controls.Add(modeSectionCaptionLabel);

        statusGroupBox = CreateCard();
        statusSectionCaptionLabel = CreateSectionCaption("СТАН");
        localizationStateLabel = new Label
        {
            Text = "Не визначено",
            AutoSize = true,
            Dock = DockStyle.Top,
            Font = new System.Drawing.Font("Segoe UI", 11F, FontStyle.Bold),
            ForeColor = UiTheme.PrimaryText,
            Margin = new Padding(0, 0, 0, 12)
        };
        installedCaptionLabel = CreateStatusCaption();
        installedModeLabel = CreateStatusValue();
        installedMetaLabel = CreateStatusMeta();
        targetCaptionLabel = CreateStatusCaption();
        targetModeLabel = CreateStatusValue();
        targetMetaLabel = CreateStatusMeta();
        installedInfoLabel = installedModeLabel;
        detailsLabel = targetModeLabel;

        var progressPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            RowCount = 1,
            AutoSize = true,
            Margin = new Padding(0, 12, 0, 0)
        };
        progressPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        progressPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        progressBar = new BdoProgressBar
        {
            Dock = DockStyle.Fill,
            Value = 0,
            Height = 20,
            Margin = new Padding(0, 0, 8, 0)
        };
        progressLabel = new Label
        {
            Text = "0%",
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = UiTheme.SecondaryText,
            Margin = new Padding(0, 2, 0, 0)
        };
        progressPanel.Controls.Add(progressBar, 0, 0);
        progressPanel.Controls.Add(progressLabel, 1, 0);

        messageTextBox = new TextBox
        {
            Dock = DockStyle.Top,
            Multiline = true,
            ReadOnly = true,
            BorderStyle = BorderStyle.FixedSingle,
            ScrollBars = ScrollBars.Vertical,
            MinimumSize = new Size(0, 48),
            Height = 48,
            Margin = new Padding(0, 12, 0, 0)
        };
        var statusLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 1,
            RowCount = 9,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0)
        };
        statusLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        for (var i = 0; i < 8; i++)
            statusLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        statusLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        statusLayout.Controls.Add(localizationStateLabel, 0, 0);
        statusLayout.Controls.Add(installedCaptionLabel, 0, 1);
        statusLayout.Controls.Add(installedModeLabel, 0, 2);
        statusLayout.Controls.Add(installedMetaLabel, 0, 3);
        statusLayout.Controls.Add(targetCaptionLabel, 0, 4);
        statusLayout.Controls.Add(targetModeLabel, 0, 5);
        statusLayout.Controls.Add(targetMetaLabel, 0, 6);
        statusLayout.Controls.Add(progressPanel, 0, 7);
        statusLayout.Controls.Add(messageTextBox, 0, 8);
        statusGroupBox.Controls.Add(statusLayout);
        statusGroupBox.Controls.Add(statusSectionCaptionLabel);

        footerPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(0, 12, 0, 0),
            Margin = new Padding(0)
        };
        footerPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        footerPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
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
            Margin = new Padding(0)
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
            Margin = new Padding(0, 0, 8, 0)
        };
        leftActionsPanel.Controls.Add(installButton);

        rightActionsPanel = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Anchor = AnchorStyles.Right | AnchorStyles.Top,
            Margin = new Padding(0)
        };
        rightActionsPanel.Controls.AddRange(new Control[] { restoreOriginalButton, cancelButton });
        footerPanel.Controls.Add(leftActionsPanel, 0, 0);
        footerPanel.Controls.Add(rightActionsPanel, 1, 0);

        mainLayoutPanel.Controls.Add(headerPanel, 0, 0);
        mainLayoutPanel.Controls.Add(gameGroupBox, 0, 1);
        mainLayoutPanel.Controls.Add(modeGroupBox, 0, 2);
        mainLayoutPanel.Controls.Add(statusGroupBox, 0, 3);
        mainLayoutPanel.Controls.Add(footerPanel, 0, 4);
        rootScrollPanel = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = UiTheme.Background,
            Padding = new Padding(0)
        };
        rootScrollPanel.Controls.Add(mainLayoutPanel);
        Controls.Add(rootScrollPanel);
        ResumeLayout(false);
    }

    private static Panel CreateCard() => new()
    {
        Dock = DockStyle.Top,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        BackColor = UiTheme.PanelBackground,
        BorderStyle = BorderStyle.FixedSingle,
        Padding = new Padding(16, 30, 16, 16),
        Margin = new Padding(0, 0, 0, 12)
    };

    private static Label CreateSectionCaption(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Dock = DockStyle.Top,
        Font = new System.Drawing.Font("Segoe UI", 8.5F, FontStyle.Bold),
        ForeColor = UiTheme.SecondaryText,
        BackColor = Color.Transparent,
        Margin = new Padding(0, 0, 0, 12)
    };

    private static Label CreateStatusCaption() => new()
    {
        Text = "",
        AutoSize = true,
        Dock = DockStyle.Top,
        Font = new System.Drawing.Font("Segoe UI", 8.5F, FontStyle.Bold),
        ForeColor = UiTheme.SecondaryText,
        Margin = new Padding(0, 4, 0, 2),
        Visible = false
    };

    private static Label CreateStatusValue() => new()
    {
        Text = "",
        AutoSize = true,
        Dock = DockStyle.Top,
        Font = new System.Drawing.Font("Segoe UI", 9.5F),
        ForeColor = UiTheme.PrimaryText,
        MaximumSize = new Size(700, 0),
        Margin = new Padding(0, 0, 0, 2),
        Visible = false
    };

    private static Label CreateStatusMeta() => new()
    {
        Text = "",
        AutoSize = true,
        Dock = DockStyle.Top,
        Font = new System.Drawing.Font("Segoe UI", 8.5F),
        ForeColor = UiTheme.SecondaryText,
        MaximumSize = new Size(700, 0),
        Margin = new Padding(0, 0, 0, 2),
        Visible = false
    };
}
