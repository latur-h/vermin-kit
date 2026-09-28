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
    readonly NotesOverlayForm notesForm;
    readonly NotifyIcon notifyIcon;
    readonly Icon trayIcon;
    readonly System.Windows.Forms.Timer placementTimer;

    volatile bool tagging;
    volatile bool settingsOpen;
    volatile bool notesOpen;
    volatile bool resumeTagging;
    int tagGeneration;
    int resumeToken;
    Task? tagTask;
    bool stopped;
    WindowBounds? lastBounds;
    Point statusPointShown;
    Size statusSizeShown;
    bool statusVisible;
    Point settingsPointShown;
    Size settingsSizeShown;
    bool settingsVisible;
    Point notesPointShown;
    Size notesSizeShown;
    bool notesVisible;
    bool shownTagging;
    TagSnapshot shownSnapshot;
    bool dragging;
    bool placedOnDesktop;
    Point dragCursorStart;
    Point dragStatusStart;

    public TrayApplication()
    {
        settingsForm = new SettingsOverlayForm(settings, inputSimulator);
        settingsForm.DragStart += BeginDrag;
        notesForm = new NotesOverlayForm(new LoadoutBook(GameCatalog.Load()));
        notesForm.CloseRequested += CloseNotes;
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
            hotkeys.Register("notes", ToggleNotes, TagSettings.NotesKey);
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
        if (!HotkeysAllowed() || notesOpen || tagging)
            return Task.CompletedTask;

        return StartTagging();
    }

    Task StartTagging()
    {
        var generation = Interlocked.Increment(ref tagGeneration);
        tagging = true;
        tagTask = RunTag(generation);
        return tagTask;
    }

    async Task RunTag(int generation)
    {
        try
        {
            await TagLoop().ConfigureAwait(false);
        }
        finally
        {
            if (generation == Volatile.Read(ref tagGeneration))
                tagging = false;
        }
    }

    Task Deactivate()
    {
        if (!HotkeysAllowed())
            return Task.CompletedTask;

        CancelTaggingResume();
        tagging = false;
        return Task.CompletedTask;
    }

    void PauseTagging()
    {
        if (!tagging)
            return;

        resumeTagging = true;
        tagging = false;
    }

    void CancelTaggingResume()
    {
        resumeTagging = false;
        resumeToken++;
    }

    void ResumeTagging()
    {
        if (!resumeTagging)
            return;

        var token = resumeToken;
        var pending = tagTask;
        _ = FinishResume(pending, token);
    }

    async Task FinishResume(Task? pending, int token)
    {
        if (pending is not null)
        {
            try
            {
                await pending.ConfigureAwait(true);
            }
            catch (Exception)
            {
            }
        }

        if (token != resumeToken || !resumeTagging || notesOpen)
            return;

        resumeTagging = false;
        if (HotkeysAllowed())
            _ = StartTagging();
    }

    Task ToggleEdit()
    {
        if (!HotkeysAllowed())
            return Task.CompletedTask;

        settingsOpen = !settingsOpen;
        return Task.CompletedTask;
    }

    Task ToggleNotes()
    {
        if (HotkeysAllowed())
        {
            notesOpen = !notesOpen;
            if (notesOpen)
                PauseTagging();
            else
            {
                ResumeTagging();
                if (inputSimulator.IsActiveWindow() || IsThisProcessForeground())
                    inputSimulator.WinActivate(ProcessName);
            }
            return Task.CompletedTask;
        }

        if (notesOpen && IsThisProcessForeground())
            CloseNotes();
        return Task.CompletedTask;
    }

    void CloseNotes()
    {
        notesOpen = false;
        ResumeTagging();
        if (IsThisProcessForeground())
            inputSimulator.WinActivate(ProcessName);
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

#if !DEBUG
        bool keep = inputSimulator.IsActiveWindow() || IsThisProcessForeground();
        if (!keep)
        {
            lastBounds = null;
            if (dragging)
            {
                dragging = false;
                placementTimer.Interval = 100;
            }
            ConcealOverlays();
            return;
        }
#endif

        placedOnDesktop = false;
        WindowBounds bounds;
        if (lastBounds is { } game && Usable(game))
            bounds = game;
#if DEBUG
        else
        {
            bounds = DesktopBounds();
            placedOnDesktop = true;
        }
#else
        else
        {
            if (dragging)
            {
                dragging = false;
                placementTimer.Interval = 100;
            }
            ConcealOverlays();
            return;
        }
#endif

        if (notesOpen && dragging)
        {
            dragging = false;
            placementTimer.Interval = 100;
        }

        if (dragging)
        {
            TrackDrag(bounds);
            if (dragging)
                return;
        }

        var statusSize = statusForm.Measure();
        var statusPoint = settings.ResolveStatus(bounds, statusSize);
        if (notesOpen)
        {
            if (statusVisible)
            {
                statusForm.Conceal();
                statusVisible = false;
            }
        }
        else
        {
            Place(statusForm, statusPoint, statusSize, ref statusPointShown, ref statusSizeShown, ref statusVisible);
        }

        if (!settingsOpen)
        {
            if (settingsVisible)
            {
                settingsForm.Conceal();
                settingsVisible = false;
            }
        }
        else
        {
            if (!settingsVisible)
                settingsForm.Sync();

            var settingsSize = settingsForm.ClientSize;
            var settingsPoint = OverlayPlacement.PlaceEditor(bounds, statusPoint, statusSize, settingsSize);
            Place(settingsForm, settingsPoint, settingsSize, ref settingsPointShown, ref settingsSizeShown, ref settingsVisible);
        }

        if (!notesOpen)
        {
            if (notesVisible)
            {
                notesForm.Conceal();
                notesVisible = false;
            }
            return;
        }

        var notesSize = new Size(Math.Max(1, bounds.Width), Math.Max(1, bounds.Height));
        var notesPoint = new Point(bounds.Left, bounds.Top);
        Place(notesForm, notesPoint, notesSize, ref notesPointShown, ref notesSizeShown, ref notesVisible);
        if (settingsVisible)
            notesForm.BringAbove();
        return;

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
        if (!placedOnDesktop)
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

        if (notesVisible)
        {
            notesForm.Conceal();
            notesVisible = false;
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

    bool HotkeysAllowed()
    {
#if DEBUG
        return true;
#else
        return inputSimulator.IsActiveWindow();
#endif
    }

    static bool Usable(WindowBounds bounds) =>
        bounds.Width >= 50 && bounds.Height >= 50 && bounds.Left >= -1000 && bounds.Top >= -1000;

    static WindowBounds DesktopBounds()
    {
        var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);
        return new WindowBounds(area.Left, area.Top, area.Right, area.Bottom);
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
        notesForm.CloseForExit();
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
