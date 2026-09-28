using System.Runtime.InteropServices;

namespace VerminKit;

sealed class ChoiceList : ListBox
{
    const int WM_LBUTTONDOWN = 0x0201;
    const int WM_LBUTTONUP = 0x0202;
    const int SB_VERT = 1;

    int armed = -1;

    bool hiding;

    public ChoiceList()
    {
        IntegralHeight = false;
        BorderStyle = BorderStyle.None;
        DrawMode = DrawMode.OwnerDrawFixed;
        ItemHeight = 32;
        ScrollAlwaysVisible = false;
    }

    public event EventHandler? ItemPicked;

    public event EventHandler? Scrolled;

    public int VisibleRows => Math.Max(1, Height / Math.Max(1, ItemHeight));

    public void Wheel(int delta)
    {
        var step = delta < 0 ? 1 : -1;
        TopIndex = Math.Clamp(TopIndex + step, 0, Math.Max(0, Items.Count - VisibleRows));
        Scrolled?.Invoke(this, EventArgs.Empty);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_LBUTTONDOWN)
        {
            armed = IndexFromPoint(PointFrom(m.LParam));
            if (armed >= 0 && armed < Items.Count)
                SelectedIndex = armed;
            return;
        }

        if (m.Msg == WM_LBUTTONUP)
        {
            var index = IndexFromPoint(PointFrom(m.LParam));
            var pick = armed >= 0 && index == armed;
            armed = -1;
            if (pick)
                ItemPicked?.Invoke(this, EventArgs.Empty);
            return;
        }

        base.WndProc(ref m);
        if (!hiding && IsHandleCreated && m.Msg == 0x0005)
        {
            hiding = true;
            ShowScrollBar(Handle, SB_VERT, false);
            hiding = false;
        }
    }

    static Point PointFrom(IntPtr value)
    {
        var packed = unchecked((int)(nint)value);
        return new Point((short)(packed & 0xFFFF), (short)((packed >> 16) & 0xFFFF));
    }

    [DllImport("user32.dll")]
    static extern bool ShowScrollBar(IntPtr hWnd, int wBar, bool bShow);
}
