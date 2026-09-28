using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace VerminKit;

sealed class NotesEditor : UserControl
{
    const int WM_SETREDRAW = 0x000B;
    const int EM_GETOLEINTERFACE = 0x043C;
    const int EM_GETSCROLLPOS = 0x04DD;
    const int EM_SETSCROLLPOS = 0x04DE;
    const int TomSuspend = -9999995;
    const int TomResume = -9999994;

    static readonly Color Paper = KitLook.Field;
    static readonly Color Ink = KitLook.Ink;
    static readonly Color CardInk = KitLook.CardInk;
    static readonly Color Frame = KitLook.Frame;
    static readonly Font BoldFace = new(KitLook.Notes, FontStyle.Bold);

    readonly NotePad box = new()
    {
        BorderStyle = BorderStyle.None,
        Multiline = true,
        AcceptsTab = true,
        DetectUrls = false,
        ScrollBars = RichTextBoxScrollBars.None,
        WordWrap = true,
        BackColor = Paper,
        ForeColor = Ink,
        HideSelection = false
    };
    readonly GoldScrollBar bar = new();
    readonly List<(int Start, int Length)> slices = [];

    bool painting;
    int drawHold;
    bool barSync;
    bool rectActive;
    int hoverIndex = -2;
    int indentStep;
    int charWidth;
    Point rectAnchor;
    Point rectFocus;
    readonly List<(Rectangle Bounds, string Markup)> copyMarks = [];
    int copyHot = -1;
    int[] paragraphLevels = [];
    string styledText = "";
    List<NoteSpan> styledSpans = [];
    List<BoldMark> styledBold = [];
    List<(int Start, int Length)> paintedSlices = [];
    int styledCaret;
    Color caretTint;
    bool caretBold;
    int undoHold;
    ITextDocument? undoDocument;
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
            TrackLevels(styledText, box.Text);
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
        box.MouseMove += (_, args) => OnBoxMove(args.Location);
        box.AcceptCopy = TryCopy;
        box.PaintedOver += DrawCopyMarks;
        box.HandleCreated += (_, _) => RestoreIndents();
        box.MouseLeave += (_, _) =>
        {
            ClearHover();
            if (copyHot < 0)
                return;
            copyHot = -1;
            box.Cursor = Cursors.IBeam;
            box.Invalidate();
        };
        box.MouseWheel += (_, args) => OnWheel(args);
        box.PlainPaste += (_, _) => InsertClipboard();
        box.KeyDown += (_, args) => OnBoxKey(args);
        box.KeyPress += (_, args) =>
        {
            if (args.KeyChar != '*' || box.SelectionLength == 0)
                return;
            WrapStars();
            args.Handled = true;
        };
        box.KeyUp += (_, args) =>
        {
            if (!args.Control || args.KeyCode is not (Keys.Z or Keys.Y))
                return;
            paragraphLevels = ReadLevels();
            NotesChanged?.Invoke(this, EventArgs.Empty);
        };
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
        painting = true;
        try
        {
            if (!box.IsHandleCreated)
                _ = box.Handle;
        }
        finally
        {
            painting = false;
        }

        RestoreIndents();
        Colorize();
        RestoreIndents();
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
        var text = Notes.Replace(Gap.ToString(), "");
        if (pendingLevels is not null)
            paragraphLevels = pendingLevels.ToArray();
        else if (paragraphLevels.Length != ParagraphCount(text))
            paragraphLevels = ReadLevels();

        var result = new StringBuilder();
        var index = 0;
        var paragraph = 0;
        while (true)
        {
            var end = text.IndexOf('\n', index);
            if (end < 0)
                end = text.Length;
            var level = paragraph < paragraphLevels.Length ? paragraphLevels[paragraph] : 0;
            result.Append('\t', Math.Max(0, level));
            result.Append(text, index, end - index);
            if (end >= text.Length)
                break;
            result.Append('\n');
            index = end + 1;
            paragraph++;
        }

        return result.ToString();
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
        var before = styledText;
        var safeStart = Math.Clamp(start, 0, box.TextLength);
        var safeLength = Math.Clamp(length, 0, box.TextLength - safeStart);
        box.Select(safeStart, safeLength);
        box.SelectedText = text;
        painting = false;
        TrackLevels(before, box.Text);
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

        var text = box.Text;
        var caret = box.SelectionStart;
        var length = box.SelectionLength;
        var spans = new List<NoteSpan>(NoteMarkup.Find(text));
        var bold = new List<BoldMark>(NoteMarkup.FindBold(text));
        var (editAt, removed, added) = Diff(styledText, text);
        if (FormattingHolds(spans, bold, editAt, added - removed, caret) && GapsReady(text, spans, caret))
        {
            styledText = text;
            styledSpans = spans;
            styledBold = bold;
            styledCaret = caret;
            if (slices.Count == 0)
            {
                TintCaret(text, caret);
                return;
            }

            painting = true;
            HoldUndo();
            try
            {
                PaintSlices();
            }
            finally
            {
                ReleaseUndo();
                painting = false;
            }

            return;
        }

        var scroll = ScrollPoint();
        SuspendDraw();
        painting = true;
        HoldUndo();
        try
        {
            var shifted = SyncGaps(caret);
            if (shifted != caret || box.Text != text)
            {
                caret = shifted;
                length = 0;
                text = box.Text;
                spans = new List<NoteSpan>(NoteMarkup.Find(text));
                bold = new List<BoldMark>(NoteMarkup.FindBold(text));
                (editAt, removed, added) = Diff(styledText, text);
            }

            Restyle(text, spans, bold, caret, editAt, removed, added);
            PaintSlices();
            var keep = slices.Count > 0 ? 0 : length;
            var pos = Math.Clamp(caret, 0, box.TextLength);
            box.Select(pos, 0);
            var inside = NoteMarkup.SpanAt(text, pos) is not null;
            box.SelectionColor = inside ? CardInk : Ink;
            box.SelectionFont = InsideBold(bold, pos) ? BoldFace : Font;
            caretTint = inside ? CardInk : Ink;
            caretBold = InsideBold(bold, pos);
            box.Select(pos, Math.Clamp(keep, 0, Math.Max(0, box.TextLength - pos)));
            if (ScrollPoint() != scroll)
                SetScrollPoint(scroll);
            styledText = text;
            styledSpans = spans;
            styledBold = bold;
            styledCaret = caret;
        }
        finally
        {
            ReleaseUndo();
            painting = false;
            ResumeDraw();
        }
    }

    bool FormattingHolds(List<NoteSpan> spans, List<BoldMark> bold, int editAt, int delta, int caret)
    {
        if (!RangesHold(spans, editAt, delta, caret))
            return false;
        if (bold.Count != styledBold.Count)
            return false;

        for (var index = 0; index < bold.Count; index++)
        {
            var old = styledBold[index];
            var next = bold[index];
            var shift = old.Start >= editAt ? delta : 0;
            if (next.Start != old.Start + shift || next.Length != old.Length || next.Closed != old.Closed)
                return false;
            if (InsideRange(next.Start, next.Length, caret) != InsideRange(old.Start, old.Length, styledCaret))
                return false;
            if (editAt > old.Start && editAt < old.Start + old.Length)
                return false;
        }

        return true;
    }

    bool RangesHold(List<NoteSpan> spans, int editAt, int delta, int caret)
    {
        if (spans.Count != styledSpans.Count)
            return false;

        for (var index = 0; index < spans.Count; index++)
        {
            var old = styledSpans[index];
            var next = spans[index];
            var shift = old.Start >= editAt ? delta : 0;
            if (next.Start != old.Start + shift || next.Length != old.Length || next.Closed != old.Closed)
                return false;
            if (InsideSpan(next, caret) != InsideSpan(old, styledCaret))
                return false;
            if (editAt > old.Start && editAt < old.Start + old.Length)
                return false;
        }

        return true;
    }

    void Restyle(string text, List<NoteSpan> spans, List<BoldMark> bold, int caret, int editAt, int removed, int added)
    {
        var dirtyStart = editAt;
        var dirtyEnd = editAt + Math.Max(added, removed);
        for (var index = 0; index < spans.Count; index++)
        {
            var span = spans[index];
            var flipped = index < styledSpans.Count && InsideSpan(span, caret) != InsideSpan(styledSpans[index], styledCaret);
            Cover(span.Start, span.Length, flipped, ref dirtyStart, ref dirtyEnd);
        }

        for (var index = 0; index < bold.Count; index++)
        {
            var mark = bold[index];
            var flipped = index < styledBold.Count
                && InsideRange(mark.Start, mark.Length, caret) != InsideRange(styledBold[index].Start, styledBold[index].Length, styledCaret);
            Cover(mark.Start, mark.Length, flipped, ref dirtyStart, ref dirtyEnd);
        }

        dirtyStart = Math.Clamp(dirtyStart, 0, text.Length);
        dirtyEnd = Math.Clamp(dirtyEnd, dirtyStart, text.Length);
        if (dirtyEnd > dirtyStart)
        {
            box.Select(dirtyStart, dirtyEnd - dirtyStart);
            box.SelectionColor = Ink;
            box.SelectionBackColor = Paper;
            box.SelectionFont = Font;
            SetHidden(false);
        }

        foreach (var span in spans)
        {
            if (span.Length < 1 || span.Start >= text.Length)
                continue;
            if (span.Start >= dirtyEnd || span.Start + span.Length <= dirtyStart)
                continue;
            PaintSpan(text, span, caret);
        }

        foreach (var mark in bold)
        {
            if (mark.Length < 1 || mark.Start >= text.Length)
                continue;
            if (mark.Start >= dirtyEnd || mark.Start + mark.Length <= dirtyStart)
                continue;
            PaintBold(mark, caret);
        }

        static void Cover(int start, int length, bool flipped, ref int dirtyStart, ref int dirtyEnd)
        {
            var overlaps = start < dirtyEnd && start + length > dirtyStart;
            if (!flipped && !overlaps)
                return;
            dirtyStart = Math.Min(dirtyStart, start);
            dirtyEnd = Math.Max(dirtyEnd, start + length);
        }
    }

    void PaintSpan(string text, NoteSpan span, int caret)
    {
        var take = Math.Min(span.Length, text.Length - span.Start);
        if (take < 1)
            return;

        var nameStart = 0;
        var nameLength = 0;
        var collapse = span.Closed
            && !InsideSpan(span, caret)
            && NoteMarkup.TryVisibleName(text, span, out nameStart, out nameLength);
        if (!collapse)
        {
            box.Select(span.Start, take);
            box.SelectionColor = CardInk;
            SetHidden(false);
            return;
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
        var gapEnd = hiddenStart;
        var limit = span.Start + take;
        while (gapEnd < limit && text[gapEnd] == Gap)
            gapEnd++;
        if (gapEnd > hiddenStart)
        {
            box.Select(hiddenStart, gapEnd - hiddenStart);
            SetHidden(false);
            box.SelectionColor = Paper;
            box.SelectionBackColor = Paper;
            box.SelectionFont = Font;
        }

        if (gapEnd < limit)
        {
            box.Select(gapEnd, limit - gapEnd);
            SetHidden(true);
        }
    }

    void PaintBold(BoldMark mark, int caret)
    {
        var take = Math.Min(mark.Length, box.TextLength - mark.Start);
        if (take < 3 || !mark.Closed)
            return;

        var editing = InsideRange(mark.Start, mark.Length, caret);
        if (editing)
        {
            box.Select(mark.Start, 1);
            box.SelectionFont = Font;
            SetHidden(false);
            box.Select(mark.Start + 1, take - 2);
            box.SelectionFont = BoldFace;
            SetHidden(false);
            box.Select(mark.Start + take - 1, 1);
            box.SelectionFont = Font;
            SetHidden(false);
            return;
        }

        box.Select(mark.Start, 1);
        SetHidden(true);
        box.Select(mark.Start + 1, take - 2);
        box.SelectionFont = BoldFace;
        SetHidden(false);
        box.Select(mark.Start + take - 1, 1);
        SetHidden(true);
    }

    void PaintSlices()
    {
        foreach (var slice in paintedSlices)
            PaintSlice(slice, Paper);
        foreach (var slice in slices)
            PaintSlice(slice, KitLook.PickSelected);
        paintedSlices = slices.ToList();
    }

    void PaintSlice((int Start, int Length) slice, Color color)
    {
        var start = Math.Clamp(slice.Start, 0, box.TextLength);
        var take = Math.Clamp(slice.Length, 0, box.TextLength - start);
        if (take < 1)
            return;
        box.Select(start, take);
        box.SelectionBackColor = color;
    }

    void TintCaret(string text, int caret)
    {
        if (box.SelectionLength > 0)
            return;
        var color = NoteMarkup.SpanAt(text, caret) is not null ? CardInk : Ink;
        var bold = InsideBold(NoteMarkup.FindBold(text), caret);
        if (color == caretTint && bold == caretBold)
            return;

        painting = true;
        HoldUndo();
        box.SelectionColor = color;
        box.SelectionFont = bold ? BoldFace : Font;
        ReleaseUndo();
        painting = false;
        caretTint = color;
        caretBold = bold;
    }

    static bool InsideBold(IReadOnlyList<BoldMark> marks, int caret)
    {
        foreach (var mark in marks)
        {
            if (mark.Closed && InsideRange(mark.Start, mark.Length - 1, caret))
                return true;
        }

        return false;
    }

    static bool InsideSpan(NoteSpan span, int caret) =>
        InsideRange(span.Start, span.Length, caret);

    static bool InsideRange(int start, int length, int caret) =>
        caret > start && caret < start + length;

    static (int At, int Removed, int Added) Diff(string before, string after)
    {
        var prefix = 0;
        var max = Math.Min(before.Length, after.Length);
        while (prefix < max && before[prefix] == after[prefix])
            prefix++;

        var suffix = 0;
        while (suffix < before.Length - prefix && suffix < after.Length - prefix
            && before[before.Length - 1 - suffix] == after[after.Length - 1 - suffix])
            suffix++;

        return (prefix, before.Length - prefix - suffix, after.Length - prefix - suffix);
    }

    void TrackLevels(string before, string after)
    {
        if (paragraphLevels.Length != ParagraphCount(before))
        {
            paragraphLevels = ReadLevels();
            return;
        }

        var (at, removed, added) = Diff(before, after);
        var removedBreaks = CountChar(before, at, removed, '\n');
        var addedBreaks = CountChar(after, at, added, '\n');
        if (removedBreaks == 0 && addedBreaks == 0)
            return;

        if (addedBreaks == 1 && removedBreaks == 0 && added == 1)
        {
            var index = ParagraphIndex(before, at);
            var level = index < paragraphLevels.Length ? paragraphLevels[index] : 0;
            var list = paragraphLevels.ToList();
            list.Insert(Math.Min(list.Count, index + 1), level);
            paragraphLevels = list.ToArray();
            return;
        }

        if (removedBreaks == 1 && addedBreaks == 0 && removed == 1)
        {
            var index = ParagraphIndex(before, at);
            var drop = index + 1;
            if (drop >= 0 && drop < paragraphLevels.Length)
            {
                var list = paragraphLevels.ToList();
                list.RemoveAt(drop);
                paragraphLevels = list.ToArray();
                return;
            }
        }

        var paragraph = ParagraphIndex(before, at);
        var inherited = paragraph < paragraphLevels.Length ? paragraphLevels[paragraph] : 0;
        var next = new List<int>(paragraphLevels.Length + addedBreaks);
        var head = Math.Min(paragraphLevels.Length, paragraph + 1);
        for (var index = 0; index < head; index++)
            next.Add(paragraphLevels[index]);
        for (var extra = 0; extra < addedBreaks; extra++)
            next.Add(inherited);
        for (var index = paragraph + 1 + removedBreaks; index < paragraphLevels.Length; index++)
            next.Add(paragraphLevels[index]);
        paragraphLevels = next.Count == ParagraphCount(after) ? next.ToArray() : ReadLevels();
    }

    void InsertClipboard()
    {
        string text;
        try
        {
            if (!Clipboard.ContainsText())
                return;
            text = Clipboard.GetText();
        }
        catch (ExternalException)
        {
            return;
        }

        text = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        if (text.Length == 0 || !box.IsHandleCreated)
            return;

        SuspendDraw();
        try
        {
            box.SelectedText = text;
        }
        finally
        {
            ResumeDraw();
        }
    }

    int[] ReadLevels()
    {
        if (!box.IsHandleCreated)
            return paragraphLevels;

        var caret = box.SelectionStart;
        var length = box.SelectionLength;
        var scroll = ScrollPoint();
        SuspendDraw();
        painting = true;
        var step = IndentStep();
        var text = box.Text;
        var levels = new int[ParagraphCount(text)];
        var index = 0;
        var paragraph = 0;
        try
        {
            while (paragraph < levels.Length)
            {
                box.Select(Math.Min(index, box.TextLength), 0);
                levels[paragraph] = box.SelectionIndent <= 0 ? 0 : (box.SelectionIndent + step / 2) / step;
                var end = text.IndexOf('\n', index);
                if (end < 0)
                    break;
                index = end + 1;
                paragraph++;
            }

            box.Select(Math.Clamp(caret, 0, box.TextLength), Math.Clamp(length, 0, Math.Max(0, box.TextLength - caret)));
            if (ScrollPoint() != scroll)
                SetScrollPoint(scroll);
            return levels;
        }
        finally
        {
            painting = false;
            ResumeDraw();
        }
    }

    static int ParagraphCount(string text)
    {
        var count = 1;
        foreach (var ch in text)
        {
            if (ch == '\n')
                count++;
        }

        return count;
    }

    static int ParagraphIndex(string text, int index)
    {
        var count = 0;
        var end = Math.Min(index, text.Length);
        for (var at = 0; at < end; at++)
        {
            if (text[at] == '\n')
                count++;
        }

        return count;
    }

    static int CountChar(string text, int start, int length, char value)
    {
        var count = 0;
        var end = Math.Min(text.Length, start + Math.Max(0, length));
        for (var index = Math.Max(0, start); index < end; index++)
        {
            if (text[index] == value)
                count++;
        }

        return count;
    }

    void HoldUndo()
    {
        if (!box.IsHandleCreated)
            return;

        try
        {
            undoDocument ??= OpenDocument();
            if (undoHold == 0)
                undoDocument.Undo(TomSuspend);
            undoHold++;
        }
        catch (COMException)
        {
            undoDocument = null;
        }
        catch (InvalidCastException)
        {
            undoDocument = null;
        }
    }

    void ReleaseUndo()
    {
        if (undoHold == 0)
            return;

        undoHold--;
        if (undoHold > 0 || undoDocument is null)
            return;

        try
        {
            undoDocument.Undo(TomResume);
        }
        catch (COMException)
        {
            undoDocument = null;
        }
    }

    ITextDocument OpenDocument()
    {
        var unknown = IntPtr.Zero;
        SendMessage(box.Handle, EM_GETOLEINTERFACE, IntPtr.Zero, ref unknown);
        if (unknown == IntPtr.Zero)
            throw new COMException();

        try
        {
            var raw = Marshal.GetObjectForIUnknown(unknown);
            return (ITextDocument)raw;
        }
        finally
        {
            Marshal.Release(unknown);
        }
    }

    void WrapStars()
    {
        var selected = box.SelectedText.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        if (selected.Length == 0)
            return;

        var lines = selected.Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            if (lines[index].Length == 0)
                continue;
            lines[index] = "*" + lines[index] + "*";
        }

        box.SelectedText = string.Join("\n", lines);
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

        if (args.KeyCode == Keys.Enter)
        {
            InsertBreak(args.Shift);
            args.Handled = true;
            args.SuppressKeyPress = true;
            return;
        }

        if (args.KeyCode == Keys.Back && SnapToBorder())
        {
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

    void InsertBreak(bool sameParagraph)
    {
        if (box.ReadOnly)
            return;

        if (sameParagraph)
        {
            box.SelectedText = "\u000B";
            return;
        }

        box.SelectedText = "\n";
        HoldUndo();
        box.SelectionIndent = 0;
        box.SelectionHangingIndent = 0;
        ReleaseUndo();
        var paragraph = ParagraphIndex(box.Text, box.SelectionStart);
        if (paragraph >= 0 && paragraph < paragraphLevels.Length)
            paragraphLevels[paragraph] = 0;
    }

    bool SnapToBorder()
    {
        if (box.ReadOnly || box.SelectionLength > 0)
            return false;

        var start = ParagraphStart(box.SelectionStart);
        if (box.SelectionStart != start || box.SelectionIndent <= 0)
            return false;

        HoldUndo();
        box.SelectionIndent = 0;
        box.SelectionHangingIndent = 0;
        ReleaseUndo();
        var paragraph = ParagraphIndex(box.Text, start);
        if (paragraph >= 0 && paragraph < paragraphLevels.Length)
            paragraphLevels[paragraph] = 0;
        NotesChanged?.Invoke(this, EventArgs.Empty);
        return true;
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
                var next = Math.Max(0, level + direction);
                box.SelectionIndent = next * step;
                box.SelectionHangingIndent = 0;
                var paragraph = ParagraphIndex(box.Text, at);
                if (paragraph >= 0 && paragraph < paragraphLevels.Length)
                    paragraphLevels[paragraph] = next;
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

    void RestoreIndents()
    {
        if (pendingLevels is not null)
            ApplyLevels(pendingLevels);
        else if (paragraphLevels.Length > 0)
            ApplyLevels(paragraphLevels.ToList());
    }

    void ApplyLevels(List<int> levels)
    {
        if (!box.IsHandleCreated)
        {
            pendingLevels = levels;
            return;
        }

        pendingLevels = null;
        paragraphLevels = levels.ToArray();
        painting = true;
        HoldUndo();
        var step = IndentStep();
        var at = 0;
        try
        {
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
        }
        finally
        {
            ReleaseUndo();
            painting = false;
        }
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
        var at = -1;
        foreach (var span in NoteMarkup.Find(text))
        {
            if (span.Closed && caret > span.Start && caret < span.Start + span.Length)
            {
                at = span.Start;
                break;
            }
        }

        if (at < 0)
        {
            foreach (var mark in NoteMarkup.FindBold(text))
            {
                if (mark.Closed && caret > mark.Start && caret < mark.Start + mark.Length)
                {
                    at = mark.Start;
                    break;
                }
            }
        }

        if (at == revealAt)
            return false;
        revealAt = at;
        return true;
    }

    bool GapsReady(string text, List<NoteSpan> spans, int caret)
    {
        foreach (var span in spans)
        {
            if (!NoteMarkup.TryVisibleName(text, span, out var start, out var length))
                continue;
            var present = GapAt(text, start + length);
            var wanted = span.Closed && !InsideSpan(span, caret);
            if (present != wanted)
                return false;
        }

        return true;
    }

    static bool GapAt(string text, int index) =>
        index + 1 < text.Length && text[index] == Gap && text[index + 1] == Gap;

    int SyncGaps(int caret)
    {
        var text = box.Text;
        var spans = NoteMarkup.Find(text);
        for (var index = spans.Count - 1; index >= 0; index--)
        {
            var span = spans[index];
            if (!NoteMarkup.TryVisibleName(text, span, out var start, out var length))
                continue;
            var at = start + length;
            if (span.Closed && !InsideSpan(span, caret))
                caret = InsertGap(at, caret);
            else
                caret = RemoveGap(at, caret);
            text = box.Text;
        }

        return caret;
    }

    int InsertGap(int at, int caret)
    {
        if (GapAt(box.Text, at))
            return caret;

        box.Select(Math.Clamp(at, 0, box.TextLength), 0);
        box.SelectedText = "\u2003\u2003";
        return caret >= at ? caret + 2 : caret;
    }

    int RemoveGap(int at, int caret)
    {
        var text = box.Text;
        var count = 0;
        while (at + count < text.Length && text[at + count] == Gap)
            count++;
        if (count == 0)
            return caret;

        box.Select(at, count);
        box.SelectedText = "";
        if (caret >= at + count)
            return caret - count;
        return caret > at ? at : caret;
    }

    void SuspendDraw()
    {
        if (drawHold++ == 0 && box.IsHandleCreated)
            SendMessage(box.Handle, WM_SETREDRAW, 0, 0);
    }

    void ResumeDraw()
    {
        if (drawHold == 0)
            return;
        if (--drawHold > 0 || !box.IsHandleCreated)
            return;
        SendMessage(box.Handle, WM_SETREDRAW, 1, 0);
        box.Invalidate();
    }

    void OnBoxMove(Point point)
    {
        var hot = CopyAt(point);
        if (hot != copyHot)
        {
            copyHot = hot;
            box.Cursor = hot >= 0 ? Cursors.Hand : Cursors.IBeam;
            box.Invalidate();
        }

        if (hot >= 0)
        {
            ClearHover();
            return;
        }

        Hit(point);
    }

    bool TryCopy(Point point)
    {
        var index = CopyAt(point);
        if (index < 0)
            return false;

        try
        {
            Clipboard.SetText(copyMarks[index].Markup);
        }
        catch (ExternalException)
        {
        }

        return true;
    }

    int CopyAt(Point point)
    {
        for (var index = 0; index < copyMarks.Count; index++)
        {
            if (copyMarks[index].Bounds.Contains(point))
                return index;
        }

        return -1;
    }

    void DrawCopyMarks(Graphics graphics)
    {
        copyMarks.Clear();
        if (!box.IsHandleCreated || box.TextLength == 0)
            return;

        var text = box.Text;
        foreach (var span in NoteMarkup.Find(text))
        {
            if (!span.Closed || InsideSpan(span, styledCaret))
                continue;
            if (!NoteMarkup.TryVisibleName(text, span, out var start, out var length) || length < 1)
                continue;
            var gap = start + length;
            if (!GapAt(text, gap))
                continue;

            var bounds = CopyBounds(gap);
            if (bounds.Bottom < 0 || bounds.Top > box.Height)
                continue;

            var take = Math.Min(span.Length, text.Length - span.Start);
            if (take < 1)
                continue;
            copyMarks.Add((bounds, text.Substring(span.Start, take).Replace(Gap.ToString(), "")));
        }

        for (var index = 0; index < copyMarks.Count; index++)
            DrawCopyMark(graphics, copyMarks[index].Bounds, index == copyHot);
    }

    Rectangle CopyBounds(int gap)
    {
        var origin = box.GetPositionFromCharIndex(Math.Clamp(gap, 0, box.TextLength));
        var after = box.GetPositionFromCharIndex(Math.Min(box.TextLength, gap + 2));
        var size = Math.Max(14, LineHeight - 6);
        var width = after.Y == origin.Y ? Math.Max(size, after.X - origin.X) : size;
        var x = origin.X + Math.Max(0, (width - size) / 2);
        var y = origin.Y + Math.Max(0, (LineHeight - size) / 2);
        return new Rectangle(x, y, size, size);
    }

    static void DrawCopyMark(Graphics graphics, Rectangle bounds, bool hot)
    {
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var fill = new SolidBrush(hot ? KitLook.ButtonChosen : Color.FromArgb(210, 28, 18, 12));
        using var pen = new Pen(hot ? KitLook.ButtonChosenBorder : KitLook.Frame, 1.5f);
        graphics.FillRectangle(fill, bounds);
        graphics.DrawRectangle(pen, bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1);
        var page = new Rectangle(bounds.X + 4, bounds.Y + 5, 6, 8);
        var back = new Rectangle(page.X + 3, page.Y - 2, 6, 8);
        using var pageFill = new SolidBrush(KitLook.Ink);
        graphics.FillRectangle(pageFill, back);
        graphics.DrawRectangle(pen, back);
        graphics.FillRectangle(pageFill, page);
        graphics.DrawRectangle(pen, page);
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

    int revealAt = -1;

    const int EM_SETCHARFORMAT = 0x0444;
    const int SCF_SELECTION = 1;
    const uint CFM_HIDDEN = 0x00000100;
    const uint CFE_HIDDEN = 0x00000100;
    const char Gap = '\u2003';

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

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, ref IntPtr lParam);

    [DllImport("user32.dll")]
    static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [ComImport]
    [Guid("8CC497C0-A1DF-11CE-8098-00AA0047BE5D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
    interface ITextDocument
    {
        [DispId(13)]
        int Undo(int count);
    }

    sealed class NotePad : RichTextBox
    {
        public event MouseEventHandler? RectStart;

        public event MouseEventHandler? RectMove;

        public event MouseEventHandler? RectEnd;

        public event EventHandler? PlainPaste;

        public event Action<Graphics>? PaintedOver;

        public Func<Point, bool>? AcceptCopy;

        bool tracking;

        protected override void WndProc(ref Message m)
        {
            const int WM_PASTE = 0x0302;
            const int WM_PAINT = 0x000F;
            const int WM_LBUTTONDOWN = 0x0201;
            if (m.Msg == WM_PASTE)
            {
                PlainPaste?.Invoke(this, EventArgs.Empty);
                return;
            }

            if (m.Msg == WM_LBUTTONDOWN && !BoxMods() && AcceptCopy?.Invoke(MouseFrom(m).Location) == true)
                return;

            if (m.Msg == WM_PAINT)
            {
                base.WndProc(ref m);
                var dc = GetDC(Handle);
                if (dc == IntPtr.Zero)
                    return;
                try
                {
                    using var graphics = Graphics.FromHdc(dc);
                    PaintedOver?.Invoke(graphics);
                }
                finally
                {
                    ReleaseDC(Handle, dc);
                }

                return;
            }
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
