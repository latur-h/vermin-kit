using System.Diagnostics;
using System.Runtime.InteropServices;
using Poss.Win.Automation.GlobalHotKeys;
using Poss.Win.Automation.Input;

namespace VerminKit;

sealed class TrayApplication : ApplicationContext
{
    const string ProcessName = "vermintide2_dx12.exe";

    readonly CancellationTokenSource cancellation = new();
    readonly InputSimulator inputSimulator = new(ProcessName);
    readonly TagSettings settings = new();
    readonly GlobalHotKeyManager hotkeys = new(new GlobalHotKeyManagerOptions { RunMessageLoop = true });
    readonly StatusOverlayForm statusForm = new();
    readonly SettingsOverlayForm settingsForm;
    readonly NotifyIcon notifyIcon;
    readonly Icon trayIcon;
    readonly System.Windows.Forms.Timer placementTimer;

    volatile bool tagging;
    volatile bool settingsOpen;
    bool stopped;
    WindowBounds? lastBounds;
    Point statusPointShown;
    Size statusSizeShown;
    bool statusVisible;
    Point settingsPointShown;
    Size settingsSizeShown;
    bool settingsVisible;
    bool shownTagging;
    TagSnapshot shownSnapshot;
    bool dragging;
    Point dragCursorStart;
    Point dragStatusStart;

    public TrayApplication()
    {
        settingsForm = new SettingsOverlayForm(settings, inputSimulator);
        settingsForm.DragStart += BeginDrag;
        trayIcon = CreateTrayIcon();
        notifyIcon = new NotifyIcon
        {
            Icon = trayIcon,
            Visible = true,
            Text = "Vermin Kit is running"
        };

        var menu = new ContextMenuStrip();
        var exit = new ToolStripMenuItem("Exit");
        exit.Click += (_, _) => Shutdown();
        menu.Items.Add(exit);
        notifyIcon.ContextMenuStrip = menu;

        try
        {
            hotkeys.Start();
            var initial = settings.Snapshot();
            hotkeys.Register("activate", Activate, initial.ActivateKey);
            hotkeys.Register("deactivate", Deactivate, initial.DeactivateKey);
            hotkeys.Register("edit", ToggleEdit, TagSettings.EditKey);
            shownSnapshot = initial;
            statusForm.ShowStatus(false, initial.ActivateKey);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Vermin Kit", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        placementTimer = new System.Windows.Forms.Timer { Interval = 100 };
        placementTimer.Tick += (_, _) =>
        {
            try
            {
                TrackGame();
            }
            catch (Exception)
            {
                ConcealOverlays();
            }
        };
        placementTimer.Start();
    }

    Task Activate()
    {
        if (!inputSimulator.IsActiveWindow() || tagging)
            return Task.CompletedTask;

        tagging = true;
        return RunTag();
    }

    async Task RunTag()
    {
        try
        {
            await TagLoop().ConfigureAwait(false);
        }
        finally
        {
            tagging = false;
        }
    }

    Task Deactivate()
    {
        if (!inputSimulator.IsActiveWindow())
            return Task.CompletedTask;

        tagging = false;
        return Task.CompletedTask;
    }

    Task ToggleEdit()
    {
        if (!inputSimulator.IsActiveWindow())
            return Task.CompletedTask;

        settingsOpen = !settingsOpen;
        return Task.CompletedTask;
    }

    async Task TagLoop()
    {
        string? held = null;
        try
        {
            while (!cancellation.IsCancellationRequested && tagging)
            {
                var snapshot = settings.Snapshot();
                if (!TagKeyHelper.TryNormalize(snapshot.Key, out var key))
                    break;

                inputSimulator.Send(TagKeyHelper.Down(key));
                held = key;
                await Task.Delay(snapshot.KeyGapMs, cancellation.Token).ConfigureAwait(false);
                inputSimulator.Send(TagKeyHelper.Up(key));
                held = null;

                if (!tagging || cancellation.IsCancellationRequested)
                    break;

                await Task.Delay(snapshot.TagDelayMs, cancellation.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (held is not null)
                inputSimulator.Send(TagKeyHelper.Up(held));
        }
    }

    void TrackGame()
    {
        var snapshot = settings.Snapshot();
        if (shownTagging != tagging || shownSnapshot != snapshot)
        {
            if (shownSnapshot.ActivateKey is not null && shownSnapshot.ActivateKey != snapshot.ActivateKey)
                hotkeys.Change("activate", snapshot.ActivateKey);
            if (shownSnapshot.DeactivateKey is not null && shownSnapshot.DeactivateKey != snapshot.DeactivateKey)
                hotkeys.Change("deactivate", snapshot.DeactivateKey);

            var actionKey = tagging ? snapshot.DeactivateKey : snapshot.ActivateKey;
            statusForm.ShowStatus(tagging, actionKey);
            if (settingsOpen)
                settingsForm.Sync();
            shownTagging = tagging;
            shownSnapshot = snapshot;
        }

        if (inputSimulator.IsActiveWindow())
        {
            var pos = inputSimulator.GetWindowPos(null);
            if (pos is { } rect)
                lastBounds = new WindowBounds(rect.Left, rect.Top, rect.Right, rect.Bottom);
        }

        bool keep = inputSimulator.IsActiveWindow() || IsThisProcessForeground();
        if (!keep || lastBounds is not { } bounds || bounds.Width < 50 || bounds.Height < 50 || bounds.Left < -1000 || bounds.Top < -1000)
        {
            if (!keep)
                lastBounds = null;
            if (dragging)
            {
                dragging = false;
                placementTimer.Interval = 100;
            }
            ConcealOverlays();
            return;
        }

        if (dragging)
        {
            TrackDrag(bounds);
            if (dragging)
                return;
        }

        var statusSize = statusForm.Measure();
        var statusPoint = settings.ResolveStatus(bounds, statusSize);
        Place(statusForm, statusPoint, statusSize, ref statusPointShown, ref statusSizeShown, ref statusVisible);

        if (!settingsOpen)
        {
            if (settingsVisible)
            {
                settingsForm.Conceal();
                settingsVisible = false;
            }
            return;
        }

        if (!settingsVisible)
            settingsForm.Sync();

        var settingsSize = settingsForm.ClientSize;
        var settingsPoint = OverlayPlacement.PlaceEditor(bounds, statusPoint, statusSize, settingsSize);
        Place(settingsForm, settingsPoint, settingsSize, ref settingsPointShown, ref settingsSizeShown, ref settingsVisible);
    }

    void BeginDrag(Point cursor)
    {
        if (dragging || lastBounds is not { } bounds)
            return;

        var statusSize = statusForm.Measure();
        dragStatusStart = settings.ResolveStatus(bounds, statusSize);
        dragCursorStart = cursor;
        dragging = true;
        placementTimer.Interval = 16;
        if (settingsVisible)
        {
            settingsForm.Conceal();
            settingsVisible = false;
        }
    }

    void TrackDrag(WindowBounds bounds)
    {
        var cursor = inputSimulator.MouseGetPos();
        var statusSize = statusForm.Measure();
        var next = OverlayPlacement.Clamp(
            bounds,
            new Point(
                dragStatusStart.X + cursor.x - dragCursorStart.X,
                dragStatusStart.Y + cursor.y - dragCursorStart.Y),
            statusSize);
        Place(statusForm, next, statusSize, ref statusPointShown, ref statusSizeShown, ref statusVisible);

        if (inputSimulator.GetKeyState("LButton"))
            return;

        dragging = false;
        placementTimer.Interval = 100;
        settings.SetOffset(next.X - bounds.Left, next.Y - bounds.Top);
    }

    void ConcealOverlays()
    {
        if (statusVisible)
        {
            statusForm.Conceal();
            statusVisible = false;
        }

        if (settingsVisible)
        {
            settingsForm.Conceal();
            settingsVisible = false;
        }
    }

    static void Place(OverlayForm form, Point point, Size size, ref Point shownPoint, ref Size shownSize, ref bool visible)
    {
        if (visible && shownPoint == point && shownSize == size)
            return;

        form.MoveTo(point.X, point.Y, size.Width, size.Height);
        shownPoint = point;
        shownSize = size;
        visible = true;
    }

    bool IsThisProcessForeground()
    {
        var foreground = inputSimulator.GetWindowProcessName();
        if (string.IsNullOrWhiteSpace(foreground))
            return false;

        var current = Process.GetCurrentProcess().ProcessName;
        return Bare(foreground).Equals(current, StringComparison.OrdinalIgnoreCase);
    }

    static string Bare(string name) =>
        name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;

    void Shutdown()
    {
        Cleanup();
        ExitThread();
    }

    void Cleanup()
    {
        if (stopped)
            return;

        stopped = true;
        tagging = false;
        cancellation.Cancel();
        placementTimer.Stop();
        placementTimer.Dispose();
        statusForm.CloseForExit();
        settingsForm.CloseForExit();
        notifyIcon.Visible = false;
        notifyIcon.Dispose();
        trayIcon.Dispose();
        hotkeys.Dispose();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            Cleanup();
        base.Dispose(disposing);
    }

    static Icon CreateTrayIcon()
    {
        using var bitmap = new Bitmap(32, 32, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            graphics.Clear(Color.Transparent);
            using var brush = new SolidBrush(Color.FromArgb(176, 52, 40));
            graphics.FillEllipse(brush, 1, 1, 30, 30);
            using var font = new Font("Segoe UI", 14f, FontStyle.Bold, GraphicsUnit.Pixel);
            using var text = new SolidBrush(Color.White);
            using var format = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };
            graphics.DrawString("V", font, text, new RectangleF(0, 0, 32, 32), format);
        }

        var handle = bitmap.GetHicon();
        try
        {
            using var icon = Icon.FromHandle(handle);
            return (Icon)icon.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    static extern bool DestroyIcon(IntPtr handle);
}
