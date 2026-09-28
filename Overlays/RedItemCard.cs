using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace VerminKit;

sealed class RedItemCard : Control
{
    static readonly Color TitleColor = KitLook.CardTitleColor;
    static readonly Color SubtitleColor = KitLook.CardSubtitle;
    static readonly Color PowerColor = KitLook.CardPowerColor;
    static readonly Color LabelColor = KitLook.CardLabel;
    static readonly Color PropertyColor = KitLook.CardProperty;
    static readonly Color PlaceholderColor = KitLook.CardPlaceholder;
    static readonly Color TraitColor = KitLook.CardTrait;
    static readonly Color BodyColor = KitLook.CardBody;
    static readonly Color KeywordColor = KitLook.CardKeyword;
    static readonly Color BorderColor = KitLook.CardBorder;
    static readonly Color HoverColor = KitLook.CardHover;

    readonly Font titleFont = KitLook.CardTitle;
    readonly Font powerFont = KitLook.CardPower;
    readonly string[] fields = ["weapon", "property-a", "property-b", "trait"];
    readonly Rectangle[] hits = new Rectangle[4];

    bool itemEnabled = true;
    bool showPower;
    bool propertyASet;
    bool propertyBSet;
    bool traitSet;
    string title = "";
    string propertyA = "Choose property";
    string propertyB = "Choose property";
    string traitName = "Choose trait";
    string traitDescription = "";
    string keywords = "";
    string resource = "";
    Image? mark;
    Image? traitIcon;
    string copyMarkup = "";
    Rectangle copyHit;
    bool copyHot;
    int hover = -1;

    public RedItemCard(string slot, string slotTitle, bool hasWeapon)
    {
        Slot = slot;
        SlotTitle = slotTitle;
        HasWeapon = hasWeapon;
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint,
            true);
        BackColor = KitLook.Field;
    }

    public static Image? Backdrop { get; set; }

    public string Slot { get; }

    public string SlotTitle { get; private set; }

    public bool HasWeapon { get; }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Quiet { get; set; }

    public void SetSubtitle(string subtitle) => SlotTitle = subtitle;

    public event Action<string, Rectangle>? PartClicked;

    public void ShowItem(
        bool enabled,
        string itemTitle,
        string lineA,
        string lineB,
        bool lineASet,
        bool lineBSet,
        string trait,
        string traitText,
        bool traitChosen,
        string keywordText,
        Image? itemMark,
        Image? traitMark,
        string resourceLabel,
        bool power)
    {
        itemEnabled = enabled;
        title = itemTitle;
        propertyA = lineA;
        propertyB = lineB;
        propertyASet = lineASet;
        propertyBSet = lineBSet;
        traitName = trait;
        traitDescription = traitText;
        traitSet = traitChosen;
        keywords = keywordText;
        mark = itemMark;
        traitIcon = traitMark;
        resource = resourceLabel;
        showPower = power;
        Invalidate();
    }

    public void ShowCopy(string markup)
    {
        copyMarkup = markup ?? "";
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (Quiet)
            return;
        var onCopy = !copyHit.IsEmpty && copyHit.Contains(e.Location);
        var next = onCopy ? -1 : HitAt(e.Location);
        if (onCopy == copyHot && next == hover)
            return;
        copyHot = onCopy;
        hover = next;
        Cursor = onCopy || next >= 0 ? Cursors.Hand : Cursors.Default;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (hover < 0 && !copyHot)
            return;
        hover = -1;
        copyHot = false;
        Cursor = Cursors.Default;
        Invalidate();
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (Quiet || !itemEnabled || e.Button != MouseButtons.Left)
            return;

        if (!copyHit.IsEmpty && copyHit.Contains(e.Location))
        {
            CopyMarkup();
            return;
        }

        var index = HitAt(e.Location);
        if (index < 0)
            return;
        PartClicked?.Invoke(fields[index], hits[index]);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = e.Graphics;
        var bounds = ClientRectangle;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        if (Backdrop is not null)
            graphics.DrawImage(Backdrop, bounds);
        else
            graphics.Clear(BackColor);

        using (var shade = new SolidBrush(KitLook.CardShade))
            graphics.FillRectangle(shade, bounds);

        const int pad = 8;
        var copySize = Quiet || copyMarkup.Length == 0 ? 0 : 22;
        copyHit = copySize == 0
            ? Rectangle.Empty
            : new Rectangle(Width - pad - copySize, 8, copySize, copySize);
        var markSize = mark is null ? 0 : 36;
        var reserved = (markSize == 0 ? 0 : markSize + 6) + (copySize == 0 ? 0 : copySize + 6);
        var textWidth = Math.Max(40, Width - pad * 2 - reserved);
        var y = 6;
        hits[0] = HasWeapon ? new Rectangle(pad, y, textWidth, 22) : Rectangle.Empty;
        var subtitleRect = new Rectangle(pad, y + 22, textWidth, 16);
        y += 46;
        Rectangle powerLabel = Rectangle.Empty;
        Rectangle resourceRect = Rectangle.Empty;
        Rectangle powerRect = Rectangle.Empty;
        if (showPower)
        {
            powerLabel = new Rectangle(pad, y, textWidth, 18);
            if (resource.Length > 0)
            {
                var resourceSize = TextRenderer.MeasureText(resource, Font);
                resourceRect = new Rectangle(Width - pad - resourceSize.Width, y, resourceSize.Width, 18);
            }

            powerRect = new Rectangle(pad, y + 22, textWidth, 30);
            y += 58;
        }

        hits[1] = new Rectangle(pad, y, Width - pad * 2, 18);
        hits[2] = new Rectangle(pad, y + 20, Width - pad * 2, 18);
        y += 42;
        var keywordHeight = keywords.Length == 0 ? 0 : 34;
        var traitHeight = Math.Max(36, Height - y - keywordHeight - pad);
        hits[3] = new Rectangle(pad, y, Width - pad * 2, traitHeight);

        if (hover >= 0 && itemEnabled && !hits[hover].IsEmpty)
        {
            using var hoverBrush = new SolidBrush(HoverColor);
            graphics.FillRectangle(hoverBrush, hits[hover]);
        }

        DrawLine(graphics, title, titleFont, TitleColor, hits[0].IsEmpty ? new Rectangle(pad, 6, textWidth, 22) : hits[0]);
        if (mark is not null)
        {
            var markLeft = copySize == 0 ? Width - pad - markSize : copyHit.X - 6 - markSize;
            graphics.DrawImage(mark, markLeft, 6, markSize, markSize);
        }

        if (copySize > 0)
            DrawCopy(graphics, copyHit, copyHot);
        DrawLine(graphics, SlotTitle, Font, SubtitleColor, subtitleRect);
        if (showPower)
        {
            DrawLine(graphics, "Power", Font, LabelColor, powerLabel);
            if (!resourceRect.IsEmpty)
                DrawLine(graphics, resource, Font, LabelColor, resourceRect);
            DrawLine(graphics, "300", powerFont, PowerColor, powerRect);
        }

        if (propertyA.Length > 0)
            DrawLine(graphics, "◆  " + propertyA, Font, propertyASet ? PropertyColor : PlaceholderColor, hits[1]);
        if (propertyB.Length > 0)
            DrawLine(graphics, "◆  " + propertyB, Font, propertyBSet ? PropertyColor : PlaceholderColor, hits[2]);

        var traitTextX = pad;
        if (traitIcon is not null)
        {
            graphics.DrawImage(traitIcon, pad, hits[3].Y + 2, 22, 22);
            traitTextX += 26;
        }

        if (traitName.Length > 0)
        {
            DrawLine(
                graphics,
                traitName,
                Font,
                traitSet ? TraitColor : PlaceholderColor,
                new Rectangle(traitTextX, hits[3].Y, Width - traitTextX - pad, 18));
        }
        if (traitDescription.Length > 0)
        {
            TextRenderer.DrawText(
                graphics,
                traitDescription,
                Font,
                new Rectangle(pad, hits[3].Y + 20, Width - pad * 2, Math.Max(12, traitHeight - 22)),
                BodyColor,
                TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
        }

        if (keywordHeight > 0)
        {
            TextRenderer.DrawText(
                graphics,
                keywords,
                Font,
                new Rectangle(pad, Height - pad - keywordHeight + 4, Width - pad * 2, keywordHeight - 4),
                KeywordColor,
                TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        using (var border = new Pen(BorderColor, 2f))
            graphics.DrawRectangle(border, 1, 1, Math.Max(1, Width - 3), Math.Max(1, Height - 3));
    }

    void CopyMarkup()
    {
        if (copyMarkup.Length == 0)
            return;

        try
        {
            Clipboard.SetText(copyMarkup);
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
        }
    }

    static void DrawCopy(Graphics graphics, Rectangle bounds, bool hot)
    {
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var fill = new SolidBrush(hot ? HoverColor : Color.FromArgb(210, 28, 18, 12));
        using var pen = new Pen(hot ? KitLook.ButtonChosenBorder : KitLook.Frame, 1.5f);
        graphics.FillRectangle(fill, bounds);
        graphics.DrawRectangle(pen, bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1);
        var page = new Rectangle(bounds.X + 5, bounds.Y + 6, 8, 10);
        var back = new Rectangle(page.X + 4, page.Y - 3, 8, 10);
        using var pageFill = new SolidBrush(KitLook.Ink);
        graphics.FillRectangle(pageFill, back);
        graphics.DrawRectangle(pen, back);
        graphics.FillRectangle(pageFill, page);
        graphics.DrawRectangle(pen, page);
    }

    int HitAt(Point point)
    {
        if (!itemEnabled)
            return -1;
        for (var index = 0; index < hits.Length; index++)
        {
            if (!hits[index].IsEmpty && hits[index].Contains(point))
                return index;
        }

        return -1;
    }

    static void DrawLine(Graphics graphics, string text, Font font, Color color, Rectangle bounds)
    {
        TextRenderer.DrawText(
            graphics,
            text,
            font,
            bounds,
            color,
            TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter);
    }
}
