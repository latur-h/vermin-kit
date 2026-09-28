using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace VerminKit;

sealed class LevelBadge : Control
{
    static readonly Color RingColor = KitLook.LevelRing;
    static readonly Color InnerColor = KitLook.LevelInner;
    static readonly Color NumberColor = KitLook.LevelNumber;

    readonly Font numberFont = KitLook.Level;

    public LevelBadge(int level)
    {
        Level = level;
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint |
            ControlStyles.SupportsTransparentBackColor,
            true);
        BackColor = Color.Transparent;
    }

    public int Level { get; }

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = e.Graphics;
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        var size = Math.Min(Math.Min(Width, Height) - 4, 52);
        if (size < 16)
            return;

        var bounds = new Rectangle((Width - size) / 2, (Height - size) / 2, size, size);
        var center = new PointF(bounds.X + bounds.Width / 2f, bounds.Y + bounds.Height / 2f);
        using var tooth = new SolidBrush(RingColor);
        var toothSize = Math.Max(3, size / 9);
        for (var index = 0; index < 12; index++)
        {
            var angle = index * Math.PI / 6;
            var x = center.X + (float)Math.Cos(angle) * (size / 2f - toothSize / 2f) - toothSize / 2f;
            var y = center.Y + (float)Math.Sin(angle) * (size / 2f - toothSize / 2f) - toothSize / 2f;
            graphics.FillRectangle(tooth, x, y, toothSize, toothSize);
        }

        var ring = Rectangle.Inflate(bounds, -toothSize, -toothSize);
        using var ringPen = new Pen(RingColor, 3f);
        graphics.DrawEllipse(ringPen, ring);
        var inner = Rectangle.Inflate(ring, -5, -5);
        using var fill = new SolidBrush(InnerColor);
        graphics.FillEllipse(fill, inner);
        DrawCenteredNumber(graphics, center);
    }

    void DrawCenteredNumber(Graphics graphics, PointF center)
    {
        using var path = new GraphicsPath();
        var em = graphics.DpiY * numberFont.SizeInPoints / 72f;
        path.AddString(
            Level.ToString(),
            numberFont.FontFamily,
            (int)numberFont.Style,
            em,
            Point.Empty,
            StringFormat.GenericTypographic);
        var ink = path.GetBounds();
        if (ink.Width < 1 || ink.Height < 1)
            return;

        using var shift = new Matrix();
        shift.Translate(center.X - ink.X - ink.Width / 2f, center.Y - ink.Y - ink.Height / 2f);
        path.Transform(shift);
        using var brush = new SolidBrush(NumberColor);
        graphics.FillPath(brush, path);
    }
}

sealed class TalentCell : Control
{
    static readonly Color IdleBorder = KitLook.TalentIdleBorder;
    static readonly Color SelectedBorder = KitLook.TalentSelectedBorder;
    static readonly Color IdleFill = KitLook.TalentIdleFill;
    static readonly Color SelectedFill = KitLook.TalentSelectedFill;
    static readonly Color NameColor = KitLook.Ink;
    static readonly Color DetailColor = KitLook.TalentDetail;
    static readonly Dictionary<Image, Image> GrayIcons = new();

    string talentName = "";
    string description = "";
    Image? icon;
    bool selected;
    bool showDescription;

    public TalentCell()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint,
            true);
        BackColor = IdleFill;
        Cursor = Cursors.Hand;
    }

    public void ShowTalent(string name, string talentDescription, Image? talentIcon, bool isSelected, bool isEnabled, bool descriptions)
    {
        talentName = name;
        description = talentDescription;
        icon = talentIcon;
        selected = isSelected;
        showDescription = descriptions;
        Enabled = isEnabled;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = e.Graphics;
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        var fill = selected ? SelectedFill : IdleFill;
        using (var brush = new SolidBrush(fill))
            graphics.FillRectangle(brush, ClientRectangle);

        var border = selected ? SelectedBorder : IdleBorder;
        var thickness = selected ? 2f : 1f;
        using (var pen = new Pen(border, thickness))
            graphics.DrawRectangle(pen, 1, 1, Math.Max(1, Width - 3), Math.Max(1, Height - 3));
        using (var inner = new Pen(Color.FromArgb(selected ? 120 : 50, border), 1f))
            graphics.DrawRectangle(inner, 4, 4, Math.Max(1, Width - 9), Math.Max(1, Height - 9));

        DrawCorners(graphics, border);

        var iconSize = Math.Min(56, Math.Max(28, Height - 16));
        var iconRect = new Rectangle(8, (Height - iconSize) / 2, iconSize, iconSize);
        if (icon is not null)
        {
            var drawn = selected ? icon : Gray(icon);
            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            graphics.DrawImage(drawn, iconRect);
            using var frame = new Pen(border, 1f);
            graphics.DrawRectangle(frame, iconRect.X - 1, iconRect.Y - 1, iconRect.Width + 1, iconRect.Height + 1);
        }

        var textX = iconRect.Right + 8;
        var textWidth = Math.Max(20, Width - textX - 8);
        if (showDescription && description.Length > 0)
        {
            TextRenderer.DrawText(
                graphics,
                talentName,
                Font,
                new Rectangle(textX, 6, textWidth, 18),
                NameColor,
                TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(
                graphics,
                description,
                Font,
                new Rectangle(textX, 24, textWidth, Math.Max(12, Height - 30)),
                DetailColor,
                TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        }
        else
        {
            TextRenderer.DrawText(
                graphics,
                talentName,
                Font,
                new Rectangle(textX, 0, textWidth, Height),
                NameColor,
                TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    void DrawCorners(Graphics graphics, Color color)
    {
        using var pen = new Pen(color, 2f);
        const int length = 8;
        graphics.DrawLines(pen, new[] { new Point(2, 2 + length), new Point(2, 2), new Point(2 + length, 2) });
        graphics.DrawLines(pen, new[] { new Point(Width - 3 - length, 2), new Point(Width - 3, 2), new Point(Width - 3, 2 + length) });
        graphics.DrawLines(pen, new[] { new Point(2, Height - 3 - length), new Point(2, Height - 3), new Point(2 + length, Height - 3) });
        graphics.DrawLines(pen, new[] { new Point(Width - 3 - length, Height - 3), new Point(Width - 3, Height - 3), new Point(Width - 3, Height - 3 - length) });
    }

    static Image Gray(Image source)
    {
        if (GrayIcons.TryGetValue(source, out var cached))
            return cached;

        var bitmap = new Bitmap(source.Width, source.Height);
        using var graphics = Graphics.FromImage(bitmap);
        using var attributes = new ImageAttributes();
        attributes.SetColorMatrix(new ColorMatrix(
        [
            [0.30f, 0.30f, 0.30f, 0, 0],
            [0.59f, 0.59f, 0.59f, 0, 0],
            [0.11f, 0.11f, 0.11f, 0, 0],
            [0, 0, 0, 1, 0],
            [0, 0, 0, 0, 1]
        ]));
        graphics.DrawImage(
            source,
            new Rectangle(0, 0, bitmap.Width, bitmap.Height),
            0,
            0,
            source.Width,
            source.Height,
            GraphicsUnit.Pixel,
            attributes);
        GrayIcons[source] = bitmap;
        return bitmap;
    }
}
