namespace BdoClient;

internal sealed class BdoProgressBar : Control
{
    private readonly System.Windows.Forms.Timer _marqueeTimer;
    private ProgressBarStyle _style = ProgressBarStyle.Continuous;
    private int _value;
    private int _marqueeOffset;
    private Color _indicatorColor = UiTheme.Accent;

    public Color IndicatorColor
    {
        get => _indicatorColor;
        set
        {
            if (_indicatorColor == value) return;
            _indicatorColor = value;
            Invalidate();
        }
    }

    public ProgressBarStyle Style
    {
        get => _style;
        set
        {
            if (_style == value) return;
            _style = value;
            UpdateMarqueeTimer();
            Invalidate();
        }
    }

    public int Value
    {
        get => _value;
        set
        {
            var next = Math.Clamp(value, 0, 100);
            if (_value == next) return;
            _value = next;
            Invalidate();
        }
    }

    public int MarqueeAnimationSpeed
    {
        get => _marqueeTimer.Interval;
        set => _marqueeTimer.Interval = Math.Max(15, value <= 0 ? 30 : value);
    }

    public BdoProgressBar()
    {
        SetStyle(ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer, true);
        Height = 20;
        _marqueeTimer = new System.Windows.Forms.Timer { Interval = 30 };
        _marqueeTimer.Tick += (_, _) =>
        {
            _marqueeOffset = (_marqueeOffset + 5) % Math.Max(1, Width + 80);
            Invalidate();
        };
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var track = new Rectangle(0, 2, Math.Max(1, Width - 1), Math.Max(4, Height - 4));
        using var trackBrush = new SolidBrush(UiTheme.ControlBackground);
        using var borderPen = new Pen(UiTheme.Border);
        e.Graphics.FillRectangle(trackBrush, track);
        e.Graphics.DrawRectangle(borderPen, track);

        if (_style == ProgressBarStyle.Marquee)
        {
            var segment = new Rectangle(_marqueeOffset - 80, track.Top + 1, 80, Math.Max(2, track.Height - 1));
            using var fill = new SolidBrush(_indicatorColor);
            e.Graphics.FillRectangle(fill, Rectangle.Intersect(track, segment));
        }
        else if (_value > 0)
        {
            var fillWidth = Math.Max(1, (int)Math.Round(track.Width * (_value / 100d)));
            using var fill = new SolidBrush(_indicatorColor);
            e.Graphics.FillRectangle(fill, track.Left + 1, track.Top + 1,
                Math.Min(fillWidth, track.Width - 2), Math.Max(2, track.Height - 1));
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        UpdateMarqueeTimer();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _marqueeTimer.Stop();
            _marqueeTimer.Dispose();
        }
        base.Dispose(disposing);
    }

    private void UpdateMarqueeTimer()
    {
        if (_style == ProgressBarStyle.Marquee && IsHandleCreated && !IsDisposed)
            _marqueeTimer.Start();
        else
            _marqueeTimer.Stop();
    }
}
