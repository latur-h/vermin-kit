using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace VerminKit;

sealed class NotesEditor : UserControl
{
    const int WM_SETREDRAW = 0x000B;
    const int EM_GETSCROLLPOS = 0x04DD;
    const int EM_SETSCROLLPOS = 0x04DE;

    static readonly Color Paper = KitLook.Field;
    static readonly Color Ink = KitLook.Ink;
    static readonly Color CardInk = KitLook.CardInk;
    static readonly Color Frame = KitLook.Frame;

    readonly NotePad box = new()
    {
        BorderStyle = BorderStyle.None,
        Multiline = true,
        AcceptsTab = true,
        DetectUrls = false,
        ScrollBars = RichTextBoxScrollBars.None,
        MaxLength = 4000,
        WordWrap = true,
        BackColor = Paper,
        ForeColor = Ink,
        HideSelection = false
    };
    readonly GoldScrollBar bar = new();
    readonly List<(int Start, int Length)> slices = [];

    bool painting;
    bool barSync;
    bool rectActive;
    int hoverIndex = -2;
    int indentStep;
    int charWidth;
    Point rectAnchor;
    Point rectFocus;
    List<int>? pendingLevels;

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
            slices.Clear();
            rectActive = false;
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
        box.MouseDown += (_, args) =>
        {
            if (args.Button == MouseButtons.Left)
                ClearRect();
        };
        box.MouseMove += (_, args) => Hit(args.Location);
        box.MouseLeave += (_, _) => ClearHover();
        box.MouseWheel += (_, args) => OnWheel(args);
        box.KeyDown += (_, args) => OnBoxKey(args);
        box.RectStart += (_, args) =>
        {
            rectAnchor = args.Location;
            rectFocus = args.Location;
            rectActive = true;
            slices.Clear();
        };
        box.RectMove += (_, args) =>
        {
            rectFocus = args.Location;
            RebuildSlices();
            Colorize();
        };
        box.RectEnd += (_, args) =>
        {
            rectFocus = args.Location;
            RebuildSlices();
            rectActive = slices.Count > 0;
            Colorize();
        };
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (pendingLevels is not null)
            ApplyLevels(pendingLevels);
        Colorize();
        SyncBar();
    }

    public event EventHandler? NotesChanged;

    public event EventHandler? CaretMoved;

    public event EventHandler? EscapePressed;

    public event Action<int>? Hovered;

    public event EventHandler? HoverCleared;

    public event Action<int>? WheelPassed;

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

            Import(next);
        }
    }

    public string Export()
    {
        var text = Notes;
        if (!box.IsHandleCreated)
            return text;

        painting = true;
        var caret = box.SelectionStart;
        var length = box.SelectionLength;
        var scroll = ScrollPoint();
        try
        {
            var step = IndentStep();
            var result = new StringBuilder();
            var index = 0;
            while (true)
            {
                var end = text.IndexOf('\n', index);
                if (end < 0)
                    end = text.Length;
                box.Select(Math.Min(index, box.TextLength), 0);
                var level = box.SelectionIndent <= 0 ? 0 : (box.SelectionIndent + step / 2) / step;
                result.Append('\t', level);
                result.Append(text, index, end - index);
                if (end >= text.Length)
                    break;
                result.Append('\n');
                index = end + 1;
            }

            box.Select(Math.Clamp(caret, 0, box.TextLength), Math.Clamp(length, 0, Math.Max(0, box.TextLength - caret)));
            if (ScrollPoint() != scroll)
                SetScrollPoint(scroll);
            return result.ToString();
        }
        finally
        {
            painting = false;
        }
    }

    public void Import(string? value)
    {
        var stored = (value ?? "").Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        if (box.IsHandleCreated && Export() == stored)
        {
            Colorize();
            return;
        }

        var plain = new StringBuilder();
        var levels = new List<int>();
        var index = 0;
        while (true)
        {
            var end = stored.IndexOf('\n', index);
            if (end < 0)
                end = stored.Length;
            var at = index;
            var level = 0;
            while (at < end && stored[at] == '\t')
            {
                level++;
                at++;
            }

            levels.Add(level);
            plain.Append(stored, at, end - at);
            if (end >= stored.Length)
                break;
            plain.Append('\n');
            index = end + 1;
        }

        painting = true;
        box.Text = plain.ToString();
        painting = false;
        ApplyLevels(levels);
        Colorize();
        SyncBar();
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
        if (!box.IsHandleCreated || delta == 0)
            return false;

        var pos = ScrollPoint();
        var limit = ScrollLimit(pos);
        var goingUp = delta > 0;
        if (goingUp && pos.Y <= 1)
            return false;
        if (!goingUp && pos.Y >= limit - 1)
            return false;

        var step = Math.Max(1, LineHeight * 3);
        pos.Y = goingUp ? Math.Max(0, pos.Y - step) : Math.Min(limit, pos.Y + step);
        var before = ScrollPoint().Y;
        SetScrollPoint(pos);
        SyncBar();
        return ScrollPoint().Y != before;
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        OnWheel(e);
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
        var scroll = ScrollPoint();
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

            box.SelectAll();
            box.SelectionBackColor = Paper;
            foreach (var slice in slices)
            {
                var start = Math.Clamp(slice.Start, 0, box.TextLength);
                var take = Math.Clamp(slice.Length, 0, box.TextLength - start);
                if (take < 1)
                    continue;
                box.Select(start, take);
                box.SelectionBackColor = KitLook.PickSelected;
            }

            var keep = slices.Count > 0 ? 0 : length;
            box.Select(Math.Clamp(caret, 0, box.TextLength), 0);
            var inside = NoteMarkup.SpanAt(box.Text, caret) is not null && (keep > 0 || caret < box.TextLength);
            box.SelectionColor = inside ? CardInk : Ink;
            box.Select(Math.Clamp(caret, 0, box.TextLength), keep);
            if (ScrollPoint() != scroll)
                SetScrollPoint(scroll);
        }
        finally
        {
            SendMessage(box.Handle, WM_SETREDRAW, 1, 0);
            box.Invalidate();
            painting = false;
        }

        bool Inside(NoteSpan span) => caret > span.Start && caret < span.Start + span.Length;
    }

    void OnBoxKey(KeyEventArgs args)
    {
        if (args.KeyCode == Keys.Escape)
        {
            ClearRect();
            EscapePressed?.Invoke(this, EventArgs.Empty);
            args.Handled = true;
            args.SuppressKeyPress = true;
            return;
        }

        if (args.KeyCode == Keys.Tab)
        {
            ShiftIndent(args.Shift ? -1 : 1);
            args.Handled = true;
            args.SuppressKeyPress = true;
            return;
        }

        if (args.Shift && args.Alt && args.KeyCode is Keys.Left or Keys.Right or Keys.Up or Keys.Down)
        {
            NudgeRect(args.KeyCode);
            args.Handled = true;
            args.SuppressKeyPress = true;
            return;
        }

        if (slices.Count > 0 && args.Control && args.KeyCode == Keys.C)
        {
            CopyRect();
            args.Handled = true;
            args.SuppressKeyPress = true;
        }
    }

    void ShiftIndent(int direction)
    {
        if (box.ReadOnly)
            return;

        var step = IndentStep();
        var start = box.SelectionStart;
        var length = box.SelectionLength;
        var targets = new List<int>();
        if (slices.Count > 0)
        {
            foreach (var slice in slices)
                targets.Add(ParagraphStart(slice.Start));
        }
        else
        {
            var from = ParagraphStart(start);
            var last = Math.Max(start, start + Math.Max(0, length) - (length > 0 ? 1 : 0));
            for (var at = from; at <= ParagraphStart(last);)
            {
                targets.Add(at);
                var end = ParagraphEnd(at);
                if (end >= box.TextLength)
                    break;
                at = end + 1;
            }
        }

        painting = true;
        var scroll = ScrollPoint();
        try
        {
            var seen = new HashSet<int>();
            foreach (var at in targets)
            {
                if (!seen.Add(at) || at > box.TextLength)
                    continue;
                box.Select(at, 0);
                var level = box.SelectionIndent <= 0 ? 0 : (box.SelectionIndent + step / 2) / step;
                box.SelectionIndent = Math.Max(0, level + direction) * step;
                box.SelectionHangingIndent = 0;
            }

            box.Select(Math.Clamp(start, 0, box.TextLength), Math.Clamp(length, 0, Math.Max(0, box.TextLength - start)));
            if (ScrollPoint() != scroll)
                SetScrollPoint(scroll);
        }
        finally
        {
            painting = false;
        }

        NotesChanged?.Invoke(this, EventArgs.Empty);
    }

    void ApplyLevels(List<int> levels)
    {
        if (!box.IsHandleCreated)
        {
            pendingLevels = levels;
            return;
        }

        pendingLevels = null;
        painting = true;
        var step = IndentStep();
        var at = 0;
        foreach (var level in levels)
        {
            if (at > box.TextLength)
                break;
            box.Select(at, 0);
            box.SelectionIndent = Math.Max(0, level) * step;
            box.SelectionHangingIndent = 0;
            var end = ParagraphEnd(at);
            if (end >= box.TextLength)
                break;
            at = end + 1;
        }

        box.Select(0, 0);
        painting = false;
    }

    void NudgeRect(Keys key)
    {
        if (!rectActive)
        {
            rectAnchor = box.GetPositionFromCharIndex(Math.Clamp(box.SelectionStart, 0, Math.Max(0, box.TextLength)));
            rectFocus = new Point(rectAnchor.X + CharWidth(), rectAnchor.Y + LineHeight);
            rectActive = true;
        }
        else
        {
            var dx = key == Keys.Left ? -CharWidth() : key == Keys.Right ? CharWidth() : 0;
            var dy = key == Keys.Up ? -LineHeight : key == Keys.Down ? LineHeight : 0;
            rectFocus = new Point(rectFocus.X + dx, rectFocus.Y + dy);
        }

        RebuildSlices();
        Colorize();
    }

    void RebuildSlices()
    {
        slices.Clear();
        var left = Math.Min(rectAnchor.X, rectFocus.X);
        var right = Math.Max(rectAnchor.X, rectFocus.X);
        var top = Math.Min(rectAnchor.Y, rectFocus.Y);
        var bottom = Math.Max(rectAnchor.Y, rectFocus.Y);
        if (right - left < 2 && bottom - top < 2)
            return;

        var text = box.Text;
        var band = Math.Max(8, LineHeight);
        for (var y = top; y <= bottom; y += band)
        {
            var lineY = Math.Clamp(y + 1, 0, Math.Max(0, box.ClientSize.Height - 1));
            var start = EdgeIndex(left, lineY);
            var end = EdgeIndex(right, lineY);
            if (end < start)
                (start, end) = (end, start);
            while (end > start && start < text.Length && text[end - 1] == '\n')
                end--;
            while (end > start && start < text.Length && text[start] == '\n')
                start++;
            if (end <= start)
                continue;
            if (slices.Count > 0 && start < slices[^1].Start + slices[^1].Length)
                continue;
            slices.Add((start, end - start));
        }
    }

    int EdgeIndex(int x, int y)
    {
        var point = new Point(
            Math.Clamp(x, 0, Math.Max(0, box.ClientSize.Width - 1)),
            Math.Clamp(y, 0, Math.Max(0, box.ClientSize.Height - 1)));
        var index = Math.Clamp(box.GetCharIndexFromPosition(point), 0, box.TextLength);
        if (index >= box.TextLength)
            return box.TextLength;

        var origin = box.GetPositionFromCharIndex(index);
        if (Math.Abs(origin.Y - point.Y) > LineHeight)
            return index;

        var next = index + 1 < box.TextLength ? box.GetPositionFromCharIndex(index + 1) : origin;
        var rightEdge = next.X > origin.X ? next.X : origin.X + CharWidth();
        if (point.X >= (origin.X + rightEdge) / 2)
            return index + 1;
        return index;
    }

    void ClearRect()
    {
        if (slices.Count == 0 && !rectActive)
            return;
        slices.Clear();
        rectActive = false;
        if (!painting)
            Colorize();
    }

    void CopyRect()
    {
        var text = box.Text;
        var lines = new List<string>();
        foreach (var slice in slices)
        {
            var start = Math.Clamp(slice.Start, 0, text.Length);
            var take = Math.Clamp(slice.Length, 0, text.Length - start);
            if (take > 0)
                lines.Add(text.Substring(start, take));
        }

        if (lines.Count == 0)
            return;
        try
        {
            Clipboard.SetText(string.Join("\n", lines));
        }
        catch (ExternalException)
        {
        }
    }

    int ParagraphStart(int index)
    {
        var text = box.Text;
        index = Math.Clamp(index, 0, text.Length);
        if (index == 0)
            return 0;
        var found = text.LastIndexOf('\n', index - 1);
        return found < 0 ? 0 : found + 1;
    }

    int ParagraphEnd(int index)
    {
        var text = box.Text;
        index = Math.Clamp(index, 0, text.Length);
        var found = text.IndexOf('\n', index);
        return found < 0 ? text.Length : found;
    }

    int IndentStep()
    {
        if (indentStep > 0)
            return indentStep;
        indentStep = Math.Max(16, TextRenderer.MeasureText("    ", box.Font, Size.Empty, TextFormatFlags.NoPadding).Width);
        return indentStep;
    }

    int CharWidth()
    {
        if (charWidth > 0)
            return charWidth;
        charWidth = Math.Max(6, TextRenderer.MeasureText("n", box.Font, Size.Empty, TextFormatFlags.NoPadding).Width);
        return charWidth;
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

    void OnWheel(MouseEventArgs args)
    {
        if (!Wheel(args.Delta))
            WheelPassed?.Invoke(args.Delta);
        if (args is HandledMouseEventArgs handled)
            handled.Handled = true;
    }

    void SyncBar()
    {
        if (barSync || !box.IsHandleCreated)
            return;

        var pos = ScrollPoint();
        var limit = ScrollLimit(pos);
        var view = Math.Max(1, box.ClientSize.Height);
        barSync = true;
        bar.SetRange(limit + view, view, pos.Y);
        barSync = false;
    }

    void ScrollTo(int pixels)
    {
        if (barSync || !box.IsHandleCreated)
            return;

        var pos = ScrollPoint();
        pos.Y = Math.Max(0, pixels);
        SetScrollPoint(pos);
        SyncBar();
    }

    int ScrollLimit(Point pos)
    {
        var end = box.GetPositionFromCharIndex(box.TextLength);
        var content = pos.Y + Math.Max(0, end.Y) + LineHeight;
        return Math.Max(0, content - box.ClientSize.Height);
    }

    Point ScrollPoint()
    {
        var pos = new Point();
        if (box.IsHandleCreated)
            SendMessage(box.Handle, EM_GETSCROLLPOS, IntPtr.Zero, ref pos);
        return pos;
    }

    void SetScrollPoint(Point pos)
    {
        if (!box.IsHandleCreated)
            return;

        SendMessage(box.Handle, EM_SETSCROLLPOS, IntPtr.Zero, ref pos);
    }

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

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, ref Point lParam);

    sealed class NotePad : RichTextBox
    {
        public event MouseEventHandler? RectStart;

        public event MouseEventHandler? RectMove;

        public event MouseEventHandler? RectEnd;

        bool tracking;

        protected override void WndProc(ref Message m)
        {
            const int WM_LBUTTONDOWN = 0x0201;
            const int WM_MOUSEMOVE = 0x0200;
            const int WM_LBUTTONUP = 0x0202;
            if (m.Msg == WM_LBUTTONDOWN && BoxMods())
            {
                tracking = true;
                Capture = true;
                RectStart?.Invoke(this, MouseFrom(m));
                return;
            }

            if (tracking && m.Msg == WM_MOUSEMOVE)
            {
                RectMove?.Invoke(this, MouseFrom(m));
                return;
            }

            if (tracking && m.Msg == WM_LBUTTONUP)
            {
                tracking = false;
                Capture = false;
                RectEnd?.Invoke(this, MouseFrom(m));
                return;
            }

            base.WndProc(ref m);
        }

        static bool BoxMods() =>
            (ModifierKeys & (Keys.Shift | Keys.Alt)) == (Keys.Shift | Keys.Alt);

        static MouseEventArgs MouseFrom(Message message)
        {
            var packed = unchecked((int)(nint)message.LParam);
            return new MouseEventArgs(
                MouseButtons.Left,
                1,
                (short)(packed & 0xFFFF),
                (short)((packed >> 16) & 0xFFFF),
                0);
        }
    }
}
