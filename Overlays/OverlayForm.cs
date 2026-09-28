using System.Runtime.InteropServices;

namespace VerminKit;

class OverlayForm : Form
{
    const int WS_EX_TOOLWINDOW = 0x00000080;
    const int WS_EX_TOPMOST = 0x00000008;
    const int WS_EX_NOACTIVATE = 0x08000000;
    const int WM_MOUSEACTIVATE = 0x0021;
    const int MA_NOACTIVATE = 3;
    const uint SWP_NOSIZE = 0x0001;
    const uint SWP_NOMOVE = 0x0002;
    const uint SWP_NOACTIVATE = 0x0010;
    const uint SWP_SHOWWINDOW = 0x0040;
    const uint SWP_HIDEWINDOW = 0x0080;

    static readonly IntPtr HWND_TOPMOST = new(-1);

    bool allowClose;

    protected virtual bool AllowsActivation => false;

    protected OverlayForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        ShowIcon = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        ControlBox = false;
        AutoScaleMode = AutoScaleMode.None;
        BackColor = KitLook.Chrome;
        ForeColor = KitLook.ChromeText;
        Font = KitLook.Overlay;
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ExStyle |= WS_EX_TOOLWINDOW | WS_EX_TOPMOST;
            if (!AllowsActivation)
                parameters.ExStyle |= WS_EX_NOACTIVATE;
            return parameters;
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (!AllowsActivation && m.Msg == WM_MOUSEACTIVATE)
        {
            m.Result = (IntPtr)MA_NOACTIVATE;
            return;
        }

        base.WndProc(ref m);
    }

    protected override void OnControlAdded(ControlEventArgs e)
    {
        base.OnControlAdded(e);
        if (e.Control is Label label)
            label.BackColor = BackColor;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!allowClose)
            e.Cancel = true;
        base.OnFormClosing(e);
    }

    public void MoveTo(int x, int y, int width, int height)
    {
        if (width < 1 || height < 1)
            return;

        if (!IsHandleCreated)
            CreateHandle();

        Bounds = new Rectangle(x, y, width, height);
        if (!Visible)
        {
            Show();
            PerformLayout();
            Refresh();
        }

        SetWindowPos(Handle, HWND_TOPMOST, x, y, width, height, SWP_NOACTIVATE | SWP_SHOWWINDOW);
    }

    public virtual void Conceal()
    {
        if (!IsHandleCreated)
            return;

        SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_HIDEWINDOW);
    }

    public void BringAbove()
    {
        if (!IsHandleCreated || !Visible)
            return;

        SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
    }

    public void CloseForExit()
    {
        allowClose = true;
        Close();
    }

    protected void AcceptMouse()
    {
        if (!IsHandleCreated)
            return;

        var style = ReadExStyle(Handle);
        var cleared = style & ~WS_EX_NOACTIVATE;
        if (cleared == style)
            return;

        WriteExStyle(Handle, cleared);
    }

    const int GWL_EXSTYLE = -20;

    static long ReadExStyle(IntPtr handle) =>
        IntPtr.Size == 8 ? GetWindowLongPtr64(handle, GWL_EXSTYLE).ToInt64() : GetWindowLong32(handle, GWL_EXSTYLE);

    static void WriteExStyle(IntPtr handle, long style)
    {
        if (IntPtr.Size == 8)
            SetWindowLongPtr64(handle, GWL_EXSTYLE, new IntPtr(style));
        else
            SetWindowLong32(handle, GWL_EXSTYLE, (int)style);
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLong", SetLastError = true)]
    static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong", SetLastError = true)]
    static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr", SetLastError = true)]
    static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr", SetLastError = true)]
    static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);
}

sealed class NoFocusButton : Button
{
    const int WM_MOUSEACTIVATE = 0x0021;
    const int MA_NOACTIVATE = 3;

    public NoFocusButton()
    {
        SetStyle(ControlStyles.Selectable, false);
        TabStop = false;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderColor = Color.FromArgb(70, 70, 70);
        FlatAppearance.MouseOverBackColor = Color.FromArgb(64, 64, 64);
        FlatAppearance.MouseDownBackColor = Color.FromArgb(84, 84, 84);
        BackColor = Color.FromArgb(48, 48, 48);
        ForeColor = Color.FromArgb(235, 235, 235);
        Cursor = Cursors.Hand;
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_MOUSEACTIVATE)
        {
            m.Result = (IntPtr)MA_NOACTIVATE;
            return;
        }

        base.WndProc(ref m);
    }
}

class BoardButton : Button
{
    const int WM_MOUSEACTIVATE = 0x0021;
    const int MA_ACTIVATE = 1;

    public BoardButton()
    {
        FlatStyle = FlatStyle.Flat;
        Cursor = Cursors.Hand;
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_MOUSEACTIVATE)
        {
            m.Result = (IntPtr)MA_ACTIVATE;
            return;
        }

        base.WndProc(ref m);
    }
}
