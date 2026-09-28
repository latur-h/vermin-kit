namespace VerminKit;

sealed class StatusOverlayForm : OverlayForm
{
    readonly Label label = new()
    {
        AutoSize = true,
        Location = new Point(12, 8),
        ForeColor = KitLook.StatusOn
    };

    const int WM_NCHITTEST = 0x0084;
    const int HTTRANSPARENT = -1;

    public StatusOverlayForm() : base()
    {
        label.Font = Font;
        Controls.Add(label);
        ShowStatus(false, TagSettings.DefaultActivateKey);
    }

    public void ShowStatus(bool tagging, string actionKey)
    {
        label.Text = tagging ? $"On   {actionKey}" : $"Off   {actionKey}";
        label.ForeColor = tagging
            ? KitLook.StatusOff
            : KitLook.StatusOn;
        Invalidate(true);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_NCHITTEST)
        {
            m.Result = (IntPtr)HTTRANSPARENT;
            return;
        }

        base.WndProc(ref m);
    }

    public Size Measure()
    {
        var text = TextRenderer.MeasureText(label.Text, label.Font, Size.Empty, TextFormatFlags.NoPadding);
        return new Size(text.Width + 24, text.Height + 16);
    }
}
