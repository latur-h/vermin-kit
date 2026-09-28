using System.ComponentModel;
using System.Runtime.InteropServices;

namespace VerminKit;

sealed class NotesEditor : UserControl
{
    const int WM_SETREDRAW = 0x000B;
    const int EM_LINESCROLL = 0x00B6;
    const int EM_GETFIRSTVISIBLELINE = 0x00CE;

    static readonly Color Paper = KitLook.Field;
    static readonly Color Ink = KitLook.Ink;
    static readonly Color CardInk = KitLook.CardInk;
    static readonly Color Frame = KitLook.Frame;

    readonly RichTextBox box = new()
    {
        BorderStyle = BorderStyle.None,
        Multiline = true,
        AcceptsTab = false,
        DetectUrls = false,
        ScrollBars = RichTextBoxScrollBars.None,
        MaxLength = 4000,
        WordWrap = true,
        BackColor = Paper,
        ForeColor = Ink,
        HideSelection = false
    };
    readonly GoldScrollBar bar = new();

    bool painting;
    bool barSync;
    int hoverIndex = -2;

    public NotesEditor()
    {
        SetStyle(ControlStyles.ResizeRedraw, true);
        BackColor = Frame;
        Font = KitLook.Notes;
        box.Font = Font;
        Controls.Add(box);
        Controls.Add(bar);
        bar.ValueChanged += (_, _) => ScrollTo(bar.Value);
        box.TextChanged += (_, _) =>
        {
            if (painting)
                return;
            Colorize();
            NotesChanged?.Invoke(this, EventArgs.Empty);
            SyncBar();
        };
        box.SelectionChanged += (_, _) =>
        {
            if (painting)
                return;
            if (CollapseChanged())
                Colorize();
            CaretMoved?.Invoke(this, EventArgs.Empty);
        };
        box.MouseMove += (_, args) => Hit(args.Location);
        box.MouseLeave += (_, _) => ClearHover();
        box.KeyDown += (_, args) =>
        {
            if (args.KeyCode != Keys.Escape)
                return;
            EscapePressed?.Invoke(this, EventArgs.Empty);
            args.Handled = true;
            args.SuppressKeyPress = true;
        };
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Colorize();
        SyncBar();
    }

    public event EventHandler? NotesChanged;

    public event EventHandler? CaretMoved;

    public event EventHandler? EscapePressed;

    public event Action<int>? Hovered;

    public event EventHandler? HoverCleared;

    public int Caret => box.SelectionStart;

    public int LineHeight => Math.Max(16, box.Font.Height);

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Notes
    {
        get => box.Text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        set
        {
            var next = (value ?? "").Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
            if (Notes == next)
            {
                Colorize();
                return;
            }

            painting = true;
            box.Text = next;
            painting = false;
            Colorize();
            SyncBar();
        }
    }

    public void SetEditable(bool value)
    {
        box.ReadOnly = !value;
        box.BackColor = Paper;
    }

    public void Replace(int start, int length, string text)
    {
        painting = true;
        var safeStart = Math.Clamp(start, 0, box.TextLength);
        var safeLength = Math.Clamp(length, 0, box.TextLength - safeStart);
        box.Select(safeStart, safeLength);
        box.SelectedText = text;
        painting = false;
        Colorize();
        SyncBar();
        NotesChanged?.Invoke(this, EventArgs.Empty);
        CaretMoved?.Invoke(this, EventArgs.Empty);
        box.Focus();
    }

    public bool Wheel(int delta)
    {
        var first = FirstLine();
        var last = Math.Max(0, LineCount() - VisibleLines());
        var goingUp = delta > 0;
        if (goingUp && first <= 0)
            return false;
        if (!goingUp && first >= last)
            return false;

        ScrollBy(goingUp ? -3 : 3);
        SyncBar();
        return FirstLine() != first;
    }

    public Rectangle CaretScreenRect()
    {
        var origin = box.GetPositionFromCharIndex(Math.Clamp(box.SelectionStart, 0, Math.Max(0, box.TextLength)));
        var screen = box.PointToScreen(origin);
        return new Rectangle(screen.X, screen.Y, 4, LineHeight);
    }

    public void FocusNotes() => box.Focus();

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        bar.SetBounds(Width - 15, 1, 14, Math.Max(1, Height - 2));
        box.SetBounds(1, 1, Math.Max(1, Width - 16), Math.Max(1, Height - 2));
        SyncBar();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(Frame);
        e.Graphics.DrawRectangle(pen, 0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
    }

    void Colorize()
    {
        if (!IsHandleCreated)
            return;

        painting = true;
        var caret = box.SelectionStart;
        var length = box.SelectionLength;
        var scroll = FirstLine();
        SendMessage(box.Handle, WM_SETREDRAW, 0, 0);
        try
        {
            box.SelectAll();
            box.SelectionColor = Ink;
            SetHidden(false);
            var text = box.Text;
            foreach (var span in NoteMarkup.Find(text))
            {
                if (span.Length < 1 || span.Start >= text.Length)
                    continue;
                var take = Math.Min(span.Length, text.Length - span.Start);
                var nameStart = 0;
                var nameLength = 0;
                var collapse = span.Closed
                    && !Inside(span)
                    && NoteMarkup.TryVisibleName(text, span, out nameStart, out nameLength);
                if (!collapse)
                {
                    box.Select(span.Start, take);
                    box.SelectionColor = CardInk;
                    SetHidden(false);
                    continue;
                }

                if (nameStart > span.Start)
                {
                    box.Select(span.Start, nameStart - span.Start);
                    SetHidden(true);
                }

                box.Select(nameStart, nameLength);
                box.SelectionColor = CardInk;
                SetHidden(false);
                var hiddenStart = nameStart + nameLength;
                var hiddenEnd = span.Start + take;
                if (hiddenStart < hiddenEnd)
                {
                    box.Select(hiddenStart, hiddenEnd - hiddenStart);
                    SetHidden(true);
                }
            }

            box.Select(Math.Clamp(caret, 0, box.TextLength), 0);
            var inside = NoteMarkup.SpanAt(box.Text, caret) is not null && (length > 0 || caret < box.TextLength);
            box.SelectionColor = inside ? CardInk : Ink;
            box.Select(Math.Clamp(caret, 0, box.TextLength), length);
            var back = FirstLine();
            if (back != scroll)
                ScrollBy(scroll - back);
        }
        finally
        {
            SendMessage(box.Handle, WM_SETREDRAW, 1, 0);
            box.Invalidate();
            painting = false;
        }

        bool Inside(NoteSpan span) => caret > span.Start && caret < span.Start + span.Length;
    }

    bool CollapseChanged()
    {
        var text = box.Text;
        var caret = box.SelectionStart;
        var inside = false;
        foreach (var span in NoteMarkup.Find(text))
        {
            if (span.Closed && caret > span.Start && caret < span.Start + span.Length)
            {
                inside = true;
                break;
            }
        }

        if (inside == reveal)
            return false;
        reveal = inside;
        return true;
    }

    void SetHidden(bool hidden)
    {
        var format = new CHARFORMAT2
        {
            cbSize = Marshal.SizeOf<CHARFORMAT2>(),
            dwMask = CFM_HIDDEN,
            dwEffects = hidden ? CFE_HIDDEN : 0,
            szFaceName = ""
        };
        SendMessage(box.Handle, EM_SETCHARFORMAT, (IntPtr)SCF_SELECTION, ref format);
    }

    int LineCount() =>
        box.IsHandleCreated ? box.GetLineFromCharIndex(Math.Max(0, box.TextLength)) + 1 : 1;

    int VisibleLines() => Math.Max(1, box.ClientSize.Height / LineHeight);

    void SyncBar()
    {
        if (barSync || !box.IsHandleCreated)
            return;

        barSync = true;
        bar.SetRange(Math.Max(1, LineCount()), VisibleLines(), FirstLine());
        barSync = false;
    }

    void ScrollTo(int line)
    {
        if (barSync || !box.IsHandleCreated)
            return;

        ScrollBy(line - FirstLine());
        SyncBar();
    }

    void ScrollBy(int lines)
    {
        if (lines == 0 || !box.IsHandleCreated)
            return;

        SendMessage(box.Handle, EM_LINESCROLL, 0, lines);
    }

    int FirstLine() =>
        box.IsHandleCreated ? SendMessage(box.Handle, EM_GETFIRSTVISIBLELINE, 0, 0) : 0;

    void Hit(Point point)
    {
        var index = CharAt(point);
        if (index == hoverIndex)
            return;

        hoverIndex = index;
        if (index < 0)
            HoverCleared?.Invoke(this, EventArgs.Empty);
        else
            Hovered?.Invoke(index);
    }

    void ClearHover()
    {
        if (hoverIndex < 0)
            return;
        hoverIndex = -1;
        HoverCleared?.Invoke(this, EventArgs.Empty);
    }

    int CharAt(Point point)
    {
        if (box.TextLength == 0)
            return -1;

        var index = box.GetCharIndexFromPosition(point);
        var origin = box.GetPositionFromCharIndex(index);
        if (point.Y < origin.Y - 2 || point.Y > origin.Y + LineHeight + 2)
            return -1;

        var line = box.GetLineFromCharIndex(index);
        var start = box.GetFirstCharIndexFromLine(line);
        if (start < 0)
            return -1;

        var next = box.GetFirstCharIndexFromLine(line + 1);
        var end = next < 0 ? box.TextLength : next;
        if (end <= start)
            return index;

        var tail = box.GetPositionFromCharIndex(end - 1);
        if (point.X > tail.X + 18)
            return -1;
        return index;
    }

    bool reveal;

    const int EM_SETCHARFORMAT = 0x0444;
    const int SCF_SELECTION = 1;
    const uint CFM_HIDDEN = 0x00000100;
    const uint CFE_HIDDEN = 0x00000100;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    struct CHARFORMAT2
    {
        public int cbSize;
        public uint dwMask;
        public uint dwEffects;
        public int yHeight;
        public int yOffset;
        public int crTextColor;
        public byte bCharSet;
        public byte bPitchAndFamily;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szFaceName;
        public short wWeight;
        public short sSpacing;
        public int crBackColor;
        public int lcid;
        public int dwReserved;
        public short sStyle;
        public short wKerning;
        public byte bUnderlineType;
        public byte bAnimation;
        public byte bRevAuthor;
        public byte bReserved1;
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, ref CHARFORMAT2 format);

    [DllImport("user32.dll")]
    static extern int SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);
}
