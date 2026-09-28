using Poss.Win.Automation.Input;

namespace Vermintide_2;

sealed class SettingsOverlayForm : OverlayForm
{
    static readonly Color HintColor = Color.FromArgb(180, 180, 180);
    static readonly Color ErrorColor = Color.FromArgb(255, 150, 130);

    readonly TagSettings settings;
    readonly InputSimulator simulator;
    readonly Label onValue;
    readonly Label offValue;
    readonly Label keyValue;
    readonly Label delayValue;
    readonly Label gapValue;
    readonly Label prompt;
    readonly System.Windows.Forms.Timer captureTimer;

    CaptureSlot captureSlot;
    bool capturing;
    bool waitingRelease;
    HashSet<string> blocked = new();
    DateTime captureDeadline;

    public event Action<Point>? DragStart;

    public SettingsOverlayForm(TagSettings settings, InputSimulator simulator) : base()
    {
        this.settings = settings;
        this.simulator = simulator;
        ClientSize = new Size(272, 236);
        Text = "Tag settings";

        onValue = ValueLabel(16);
        offValue = ValueLabel(50);
        keyValue = ValueLabel(84);
        delayValue = ValueLabel(118);
        gapValue = ValueLabel(152);
        prompt = new Label
        {
            AutoSize = false,
            Location = new Point(12, 204),
            Size = new Size(248, 18),
            ForeColor = HintColor
        };

        Controls.Add(Caption("On", 12, 16));
        Controls.Add(onValue);
        AddSetButton(12, CaptureSlot.On);

        Controls.Add(Caption("Off", 12, 50));
        Controls.Add(offValue);
        AddSetButton(46, CaptureSlot.Off);

        Controls.Add(Caption("Key", 12, 84));
        Controls.Add(keyValue);
        AddSetButton(80, CaptureSlot.Tag);

        Controls.Add(Caption("Tag delay", 12, 118));
        Controls.Add(delayValue);
        AddStepper(114, () => settings.Snapshot().TagDelayMs, settings.SetTagDelayMs);

        Controls.Add(Caption("Key gap", 12, 152));
        Controls.Add(gapValue);
        AddStepper(148, () => settings.Snapshot().KeyGapMs, settings.SetKeyGapMs);

        Controls.Add(Caption("Drag to move", 12, 182));
        Controls.Add(prompt);

        captureTimer = new System.Windows.Forms.Timer { Interval = 20 };
        captureTimer.Tick += (_, _) => PollCapture();

        Sync();
    }

    public void Sync()
    {
        var snapshot = settings.Snapshot();
        onValue.Text = snapshot.ActivateKey;
        offValue.Text = snapshot.DeactivateKey;
        keyValue.Text = snapshot.Key;
        delayValue.Text = $"{snapshot.TagDelayMs} ms";
        gapValue.Text = $"{snapshot.KeyGapMs} ms";
    }

    public override void Conceal()
    {
        StopCapture();
        base.Conceal();
    }

    protected override void OnControlAdded(ControlEventArgs e)
    {
        base.OnControlAdded(e);
        if (e.Control is not Control control || control is Button)
            return;

        control.MouseDown += (_, args) => StartDrag(control, args);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        StartDrag(this, e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            captureTimer.Dispose();
        base.Dispose(disposing);
    }

    void StartDrag(Control control, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
            return;

        DragStart?.Invoke(control.PointToScreen(e.Location));
    }

    void AddSetButton(int y, CaptureSlot slot)
    {
        var button = SmallButton("Set", 196, y, 60, 26);
        button.Click += (_, _) => BeginCapture(slot);
        Controls.Add(button);
    }

    void BeginCapture(CaptureSlot slot)
    {
        captureSlot = slot;
        blocked = TagKeyHelper.KeysDown(simulator).ToHashSet();
        waitingRelease = blocked.Count > 0;
        capturing = true;
        captureDeadline = DateTime.UtcNow.AddSeconds(5);
        prompt.ForeColor = HintColor;
        prompt.Text = "Press a key";
        captureTimer.Start();
    }

    void PollCapture()
    {
        if (!capturing)
            return;

        if (DateTime.UtcNow > captureDeadline)
        {
            StopCapture();
            prompt.ForeColor = ErrorColor;
            prompt.Text = "No key pressed";
            return;
        }

        var down = TagKeyHelper.KeysDown(simulator);
        if (waitingRelease)
        {
            if (down.Any(blocked.Contains))
                return;

            waitingRelease = false;
            return;
        }

        var picked = TagKeyHelper.Pick(down);
        if (picked is null)
            return;

        var snapshot = settings.Snapshot();
        var accepted = captureSlot switch
        {
            CaptureSlot.On => settings.TrySetActivateKey(picked),
            CaptureSlot.Off => settings.TrySetDeactivateKey(picked),
            _ => settings.TrySetKey(picked)
        };
        if (!accepted)
        {
            StopCapture();
            prompt.ForeColor = ErrorColor;
            prompt.Text = FailureText(picked, snapshot);
            return;
        }

        StopCapture();
        prompt.Text = "";
        Sync();
    }

    static string FailureText(string picked, TagSnapshot snapshot)
    {
        if (!TagKeyHelper.TryNormalize(picked, out var key))
            return "Unknown key";
        if (TagSettings.IsEditKey(key))
            return "F5 is fixed";
        if (key.Equals(snapshot.ActivateKey, StringComparison.OrdinalIgnoreCase)
            || key.Equals(snapshot.DeactivateKey, StringComparison.OrdinalIgnoreCase))
            return "Already used";
        return "Unknown key";
    }

    void StopCapture()
    {
        capturing = false;
        waitingRelease = false;
        captureTimer.Stop();
    }

    void AddStepper(int y, Func<int> read, Action<int> apply)
    {
        var minus = SmallButton("-", 196, y, 28, 26);
        var plus = SmallButton("+", 228, y, 28, 26);
        minus.Click += (_, _) =>
        {
            apply(read() - 10);
            Sync();
        };
        plus.Click += (_, _) =>
        {
            apply(read() + 10);
            Sync();
        };
        Controls.Add(minus);
        Controls.Add(plus);
    }

    Label Caption(string text, int x, int y) => new()
    {
        AutoSize = true,
        Text = text,
        Location = new Point(x, y),
        ForeColor = ForeColor
    };

    Label ValueLabel(int y) => new()
    {
        AutoSize = false,
        AutoEllipsis = true,
        Location = new Point(88, y),
        Size = new Size(100, 18),
        ForeColor = ForeColor
    };

    enum CaptureSlot
    {
        Tag,
        On,
        Off
    }

    static NoFocusButton SmallButton(string text, int x, int y, int width, int height) => new()
    {
        Text = text,
        Location = new Point(x, y),
        Size = new Size(width, height)
    };
}
