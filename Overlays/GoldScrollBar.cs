using System.ComponentModel;

namespace VerminKit;

sealed class GoldScrollBar : Control
{
    static readonly Color TrackColor = Color.FromArgb(28, 18, 12);
    static readonly Color ThumbColor = Color.FromArgb(168, 118, 48);
    static readonly Color ThumbHotColor = Color.FromArgb(214, 164, 78);

    int value;
    int maximum = 1;
    int viewport = 1;
    bool dragging;
    bool hot;
    int dragOffset;

    public GoldScrollBar()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint,
            true);
        Width = 14;
        Cursor = Cursors.Hand;
    }

    public int Value => value;

    public event EventHandler? ValueChanged;

    public void SetRange(int content, int view, int current)
    {
        maximum = Math.Max(1, content);
        viewport = Math.Max(1, Math.Min(Math.Max(view, 1), maximum));
        SetValue(current, raise: true);
        Invalidate();
    }

    public void Nudge(int delta) => SetValue(value + delta, raise: true);

    public void SetValue(int next, bool raise)
    {
        next = Math.Clamp(next, 0, Math.Max(0, maximum - viewport));
        if (next == value)
            return;

        value = next;
        Invalidate();
        if (raise)
            ValueChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(TrackColor);
        var thumb = ThumbBounds();
        using var brush = new SolidBrush(hot || dragging ? ThumbHotColor : ThumbColor);
        e.Graphics.FillRectangle(brush, thumb);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left || maximum <= viewport)
            return;

        var thumb = ThumbBounds();
        if (thumb.Contains(e.Location))
        {
            dragging = true;
            dragOffset = e.Y - thumb.Y;
            Capture = true;
            return;
        }

        Nudge(e.Y < thumb.Y ? -Math.Max(1, viewport - 1) : Math.Max(1, viewport - 1));
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!dragging)
        {
            var over = ThumbBounds().Contains(e.Location);
            if (over == hot)
                return;
            hot = over;
            Invalidate();
            return;
        }

        var track = TrackBounds();
        var thumb = ThumbBounds().Height;
        var travel = Math.Max(1, track.Height - thumb);
        var y = Math.Clamp(e.Y - dragOffset, track.Y, track.Bottom - thumb);
        var span = Math.Max(1, maximum - viewport);
        SetValue((y - track.Y) * span / travel, raise: true);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        dragging = false;
        Capture = false;
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (hot)
        {
            hot = false;
            Invalidate();
        }
    }

    Rectangle TrackBounds() => new(3, 2, Math.Max(4, Width - 6), Math.Max(4, Height - 4));

    Rectangle ThumbBounds()
    {
        var track = TrackBounds();
        if (maximum <= viewport)
            return track;

        var thumb = Math.Max(28, track.Height * viewport / maximum);
        thumb = Math.Min(thumb, track.Height);
        var travel = track.Height - thumb;
        var span = Math.Max(1, maximum - viewport);
        var y = track.Y + travel * value / span;
        return new Rectangle(track.X, y, track.Width, thumb);
    }
}

sealed class BoardCanvas : Panel
{
    public BoardCanvas()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint,
            true);
        BackColor = Color.FromArgb(18, 12, 8);
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Action<Graphics, Rectangle>? PaintScene { get; set; }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        PaintScene?.Invoke(e.Graphics, ClientRectangle);
    }
}

sealed class BoardScroll : Panel
{
    readonly Panel host = new() { Dock = DockStyle.Fill, BackColor = Color.FromArgb(18, 12, 8) };

    public BoardScroll()
    {
        Dock = DockStyle.Fill;
        Document = new BoardCanvas();
        Bar = new GoldScrollBar { Dock = DockStyle.Right, Width = 14 };
        host.Controls.Add(Document);
        Controls.Add(host);
        Controls.Add(Bar);
        Bar.ValueChanged += (_, _) => MoveDocument();
        host.Resize += (_, _) => ViewResized?.Invoke(this, EventArgs.Empty);
    }

    public BoardCanvas Document { get; }

    public GoldScrollBar Bar { get; }

    public Size ViewSize => host.ClientSize;

    public event EventHandler? ViewResized;

    public void ShowDocument(int width, int height, int keep)
    {
        Document.Size = new Size(width, Math.Max(host.ClientSize.Height, height));
        Bar.SetRange(Document.Height, host.ClientSize.Height, keep);
        MoveDocument();
    }

    void MoveDocument()
    {
        var top = -Bar.Value;
        if (Document.Top == top)
            return;

        Document.Top = top;
    }
}
