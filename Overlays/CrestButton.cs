using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace VerminKit;

sealed class CrestButton : BoardButton
{
    static readonly Color IdleFill = Color.FromArgb(32, 22, 16);
    static readonly Color HotFill = Color.FromArgb(58, 38, 22);
    static readonly Color ChosenFill = Color.FromArgb(92, 48, 18);
    static readonly Color IdleBorder = Color.FromArgb(128, 92, 48);
    static readonly Color ChosenBorder = Color.FromArgb(214, 154, 58);
    static readonly Color TextColor = Color.FromArgb(236, 226, 210);
    static readonly Color DimColor = Color.FromArgb(120, 110, 100);

    bool chosen;
    bool menu;
    bool hot;
    Image? mark;

    public CrestButton()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint,
            true);
        FlatAppearance.BorderSize = 0;
        ForeColor = TextColor;
        BackColor = IdleFill;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Image? Mark
    {
        get => mark;
        set
        {
            mark = value;
            Invalidate();
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Chosen
    {
        get => chosen;
        set
        {
            if (chosen == value)
                return;
            chosen = value;
            Invalidate();
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Menu
    {
        get => menu;
        set
        {
            if (menu == value)
                return;
            menu = value;
            Invalidate();
        }
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        hot = true;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        hot = false;
        Invalidate();
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        Invalidate();
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = e.Graphics;
        var fill = !Enabled ? IdleFill : chosen ? ChosenFill : hot ? HotFill : IdleFill;
        using (var brush = new SolidBrush(fill))
            graphics.FillRectangle(brush, ClientRectangle);

        var border = chosen ? ChosenBorder : IdleBorder;
        using (var pen = new Pen(border))
            graphics.DrawRectangle(pen, 0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));

        var menuWidth = menu ? 18 : 0;
        var text = Text ?? "";
        var flags = TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis;
        var textSize = TextRenderer.MeasureText(graphics, text, Font, Size.Empty, TextFormatFlags.NoPadding);
        var icon = mark?.Width ?? 0;
        var gap = icon > 0 && text.Length > 0 ? 8 : 0;
        var block = icon + gap + textSize.Width;
        var x = Math.Max(8, (Width - menuWidth - block) / 2);
        if (mark is not null)
        {
            graphics.DrawImage(mark, x, (Height - mark.Height) / 2, mark.Width, mark.Height);
            x += mark.Width + gap;
        }

        var color = Enabled ? TextColor : DimColor;
        var limit = Math.Max(1, Width - menuWidth - x - 8);
        TextRenderer.DrawText(graphics, text, Font, new Rectangle(x, 0, limit, Height), color, flags);

        if (!menu)
            return;

        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var mid = Height / 2;
        var left = Width - 16;
        Point[] triangle = [new(left, mid - 2), new(left + 8, mid - 2), new(left + 4, mid + 3)];
        using var chevron = new SolidBrush(ChosenBorder);
        graphics.FillPolygon(chevron, triangle);
    }
}
