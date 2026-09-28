using System.Drawing.Drawing2D;

namespace VerminKit;

sealed class NotesOverlayForm : OverlayForm
{
    static readonly Color HintColor = KitLook.Hint;
    static readonly Color Paper = KitLook.Paper;
    static readonly Color Ink = KitLook.Ink;
    static readonly Color Frame = KitLook.Frame;
    static readonly int[] TalentLevels = [5, 10, 15, 20, 25, 30];

    readonly LoadoutBook book;
    readonly IconCatalog icons = new();
    readonly Font buttonFont = KitLook.Button;
    readonly bool[] careerShown = new bool[4];
    readonly ToolTip tips = new() { InitialDelay = 300, AutoPopDelay = 30000, ShowAlways = true };
    readonly Image? backdrop;
    readonly BoardScroll board = new();
    BoardCanvas content => board.Document;
    GoldScrollBar boardBar => board.Bar;
    readonly CrestButton[] heroButtons;
    readonly CrestButton[] careerButtons;
    readonly CrestButton loadoutButton;
    readonly Panel nameFrame = new() { BackColor = Frame, Padding = new Padding(1) };
    readonly TextBox nameBox = new()
    {
        MaxLength = 80,
        BorderStyle = BorderStyle.None,
        AutoSize = false
    };
    readonly NotesEditor notesEditor = new();
    readonly System.Windows.Forms.Timer noteSave = new() { Interval = 200 };
    readonly CrestButton createButton;
    readonly CrestButton deleteButton;
    readonly CheckBox descriptions = new() { Text = "Show Descriptions", AutoSize = true };
    readonly RedItemCard[] cards;
    readonly TalentCell[,] talents = new TalentCell[6, 3];
    readonly LevelBadge[] levels = new LevelBadge[6];
    readonly Panel pickerHost = new() { Visible = false, BackColor = Frame, Padding = new Padding(1) };
    readonly Label pickerCaption = new() { AutoSize = false, Height = 24, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(8, 0, 0, 0) };
    readonly ChoiceList picker = new();
    readonly GoldScrollBar pickerBar = new();
    readonly Label pickerDetail = new() { AutoSize = false, Height = 48, Padding = new Padding(8) };
    readonly Label talentsCaption = new() { Text = "Talents", AutoSize = false };
    readonly Label notesCaption = new() { Text = "Play notes", AutoSize = false };
    readonly RedItemCard hoverCard;
    readonly CardPop hoverPop;

    Rectangle pickerAnchor;
    string? pickerSlot;
    string? pickerField;
    NoteToken? noteToken;
    int hoverStart = -1;
    int suppress;
    bool layingOut;
    bool ready;
    bool pickerSync;
    Size laidOutSize;
    bool laidOutDescriptions;
    bool laidOutReady;

    public event Action? CloseRequested;

    protected override bool AllowsActivation => true;

    public NotesOverlayForm(LoadoutBook book) : base()
    {
        this.book = book;
        Text = "Notes";
        DoubleBuffered = true;
        KeyPreview = true;
        backdrop = LoadImage("background.png");
        RedItemCard.Backdrop = LoadImage("card-background.png");
        content.PaintScene = PaintScene;
        content.MouseDown += (_, _) =>
        {
            ClosePicker();
            HideHover();
        };
        Controls.Add(board);

        heroButtons = book.Catalog.Heroes.Select(hero => MakeButton(hero.Name)).ToArray();
        for (var index = 0; index < heroButtons.Length; index++)
        {
            var heroId = book.Catalog.Heroes[index].Id;
            heroButtons[index].Click += (_, _) =>
            {
                book.SelectHero(heroId);
                ClosePicker();
                RefreshBoard();
            };
            heroButtons[index].Mark = icons.Hero(heroId, 48);
            content.Controls.Add(heroButtons[index]);
        }

        careerButtons = new CrestButton[4];
        for (var index = 0; index < careerButtons.Length; index++)
        {
            careerButtons[index] = MakeButton("");
            var careerIndex = index;
            careerButtons[index].Click += (_, _) =>
            {
                var hero = book.Catalog.FindHero(book.Career.HeroId);
                if (hero is null || careerIndex >= hero.Careers.Count)
                    return;
                book.SelectCareer(hero.Careers[careerIndex].Id);
                ClosePicker();
                RefreshBoard();
            };
            content.Controls.Add(careerButtons[index]);
        }

        nameBox.TextChanged += (_, _) =>
        {
            if (Suppressed)
                return;
            book.Rename(nameBox.Text);
            RefreshLoadoutNames();
        };
        nameBox.Enter += (_, _) => ClosePicker();
        nameBox.BackColor = KitLook.Field;
        nameBox.ForeColor = Ink;
        nameBox.Font = KitLook.LoadoutName;
        nameFrame.Controls.Add(nameBox);
        notesEditor.NotesChanged += (_, _) =>
        {
            if (!Suppressed)
            {
                book.StageNotes(notesEditor.Export());
                noteSave.Stop();
                noteSave.Start();
            }

            UpdateNoteSuggest();
        };
        notesEditor.CaretMoved += (_, _) => UpdateNoteSuggest();
        notesEditor.EscapePressed += (_, _) => ClosePicker();
        notesEditor.Hovered += ShowHover;
        notesEditor.HoverCleared += (_, _) => HideHover();
        notesEditor.WheelPassed += delta => board.Bar.Nudge(delta < 0 ? 64 : -64);
        noteSave.Tick += (_, _) => FlushNotes();
        VisibleChanged += (_, _) =>
        {
            if (!Visible)
                FlushNotes();
        };
        createButton = MakeButton("New");
        createButton.Click += (_, _) =>
        {
            book.Create();
            ClosePicker();
            RefreshBoard();
            nameBox.Focus();
            nameBox.SelectAll();
        };
        deleteButton = MakeButton("Delete");
        deleteButton.Click += (_, _) =>
        {
            if (book.Current is null)
                return;
            var answer = MessageBox.Show(
                this,
                "Delete this loadout?",
                "Vermin Kit",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Warning);
            if (answer != DialogResult.OK)
                return;
            book.DeleteCurrent();
            ClosePicker();
            RefreshBoard();
        };
        loadoutButton = MakeButton("Loadout");
        loadoutButton.Menu = true;
        loadoutButton.Click += (_, _) => OpenPicker("loadout", "loadout", content.RectangleToScreen(loadoutButton.Bounds));
        var closeButton = MakeButton("Close");
        closeButton.Click += (_, _) => CloseRequested?.Invoke();
        content.Controls.Add(loadoutButton);
        content.Controls.Add(nameFrame);
        content.Controls.Add(createButton);
        content.Controls.Add(deleteButton);
        content.Controls.Add(closeButton);
        CloseButton = closeButton;

        cards =
        [
            MakeCard("primary", "Primary", true),
            MakeCard("secondary", "Secondary", true),
            MakeCard("necklace", "Necklace", false),
            MakeCard("charm", "Charm", false),
            MakeCard("trinket", "Trinket", false)
        ];

        talentsCaption.Text = "TALENTS";
        talentsCaption.Font = KitLook.Caption;
        talentsCaption.ForeColor = KitLook.TalentCaption;
        talentsCaption.TextAlign = ContentAlignment.MiddleCenter;
        talentsCaption.BackColor = KitLook.Shade;
        descriptions.Checked = book.ShowDescriptions;
        descriptions.ForeColor = KitLook.Link;
        descriptions.CheckedChanged += (_, _) =>
        {
            book.SetShowDescriptions(descriptions.Checked);
            RefreshTalents();
            LayoutBoard();
        };
        content.Controls.Add(descriptions);
        content.Controls.Add(talentsCaption);
        notesCaption.ForeColor = KitLook.Frame;
        notesCaption.BackColor = KitLook.Shade;
        notesCaption.Font = KitLook.NotesCaption;
        notesCaption.TextAlign = ContentAlignment.MiddleLeft;
        content.Controls.Add(notesCaption);
        content.Controls.Add(notesEditor);
        talentsCaption.BackColor = KitLook.Shade;
        descriptions.BackColor = KitLook.Shade;

        picker.BackColor = Paper;
        picker.ForeColor = Ink;
        picker.TabStop = false;
        pickerCaption.BackColor = Paper;
        pickerCaption.ForeColor = KitLook.Frame;
        pickerCaption.Font = KitLook.PickerCaption;
        pickerDetail.BackColor = Paper;
        pickerDetail.ForeColor = HintColor;
        pickerDetail.Font = KitLook.Ui;
        pickerDetail.UseMnemonic = false;
        pickerDetail.TextAlign = ContentAlignment.TopLeft;
        picker.ItemPicked += (_, _) =>
        {
            if (Suppressed || picker.SelectedItem is not PickChoice choice)
                return;
            if (noteToken is not null)
                ApplyNote(choice);
            else
                ApplyPick(choice);
        };
        picker.DrawItem += DrawPick;
        picker.MouseMove += (_, args) =>
        {
            if (pickerSlot == "note")
                return;
            var index = picker.IndexFromPoint(args.Location);
            var detail = index >= 0 && picker.Items[index] is PickChoice choice
                ? choice.Detail ?? ""
                : "";
            if (pickerDetail.Text == detail)
                return;
            pickerDetail.Text = detail;
            pickerDetail.Visible = detail.Length > 0;
            if (pickerHost.Visible)
                PositionPopup(pickerAnchor);
        };
        picker.Scrolled += (_, _) => SyncPickerBar();
        pickerBar.ValueChanged += (_, _) =>
        {
            if (!pickerSync)
                picker.TopIndex = pickerBar.Value;
        };
        pickerHost.Controls.Add(picker);
        pickerHost.Controls.Add(pickerBar);
        pickerHost.Controls.Add(pickerDetail);
        pickerHost.Controls.Add(pickerCaption);
        Controls.Add(pickerHost);

        for (var row = 0; row < 6; row++)
        {
            levels[row] = new LevelBadge(TalentLevels[row]);
            content.Controls.Add(levels[row]);
            for (var column = 0; column < 3; column++)
            {
                var cell = new TalentCell();
                var talentRow = row;
                var talentColumn = column;
                cell.Click += (_, _) =>
                {
                    ClosePicker();
                    book.ToggleTalent(talentRow, talentColumn);
                    RefreshTalents();
                };
                talents[row, column] = cell;
                content.Controls.Add(cell);
            }
        }

        hoverCard = new RedItemCard("note", "", true) { Quiet = true };
        hoverPop = new CardPop(hoverCard);
        KeyDown += (_, args) =>
        {
            if (args.KeyCode != Keys.Escape)
                return;
            ClosePicker();
            HideHover();
            args.Handled = true;
        };
        board.ViewResized += (_, _) => LayoutBoard();
        RefreshBoard();
        var area = Screen.PrimaryScreen?.WorkingArea.Size ?? new Size(1280, 720);
        Size = area;
        CreateHandle();
        ready = true;
        LayoutBoard();
    }

    CrestButton CloseButton { get; }

    bool Suppressed => suppress > 0;

    public override void Conceal()
    {
        HideHover();
        ClosePicker();
        base.Conceal();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        LayoutBoard();
    }

    void RefreshBoard()
    {
        suppress++;
        try
        {
            var hero = book.Catalog.FindHero(book.Career.HeroId);
            for (var index = 0; index < heroButtons.Length; index++)
                heroButtons[index].Chosen = book.Catalog.Heroes[index].Id == book.Career.HeroId;

            for (var index = 0; index < careerButtons.Length; index++)
            {
                var career = hero is not null && index < hero.Careers.Count ? hero.Careers[index] : null;
                careerShown[index] = career is not null;
                careerButtons[index].Visible = careerShown[index];
                careerButtons[index].Text = career?.Name ?? "";
                careerButtons[index].Mark = career is null ? null : icons.Career(career.Id, 56);
                careerButtons[index].Chosen = career?.Id == book.Career.Id;
            }

            loadoutButton.Text = book.Current is null ? "Loadout" : DisplayName(book.Current);

            var hasLoadout = book.Current is not null;
            nameBox.Enabled = hasLoadout;
            notesEditor.SetEditable(hasLoadout);
            deleteButton.Enabled = hasLoadout;
            descriptions.Enabled = hasLoadout;
            nameBox.Text = book.Current?.Name ?? "";
            var notes = book.Current?.Notes ?? "";
            if (notesEditor.Export() != notes)
                notesEditor.Import(notes);
            RefreshCards();
            RefreshTalents();
        }
        finally
        {
            suppress--;
        }
    }

    void RefreshLoadoutNames()
    {
        loadoutButton.Text = book.Current is null ? "Loadout" : DisplayName(book.Current);
    }

    void RefreshCards()
    {
        var enabled = book.Current is not null;
        foreach (var card in cards)
        {
            var gear = book.Gear(card.Slot);
            var weapon = book.Catalog.FindWeapon(gear?.WeaponId);
            var trait = book.Catalog.FindTrait(book.TraitGroup(card.Slot), gear?.TraitId);
            var pool = book.PropertyPool(card.Slot);
            var propertyA = book.Catalog.FindProperty(pool, gear?.PropertyA);
            var propertyB = book.Catalog.FindProperty(pool, gear?.PropertyB);
            var lineA = propertyA?.Line ?? "Choose property";
            var lineB = propertyB?.Line ?? "Choose property";
            card.ShowCopy(card.HasWeapon && weapon is not null ? NoteToken(weapon, propertyA, propertyB, trait) : "");
            card.ShowItem(
                enabled,
                card.HasWeapon ? weapon?.Name ?? "Choose weapon" : card.SlotTitle,
                lineA,
                lineB,
                gear?.PropertyA is not null,
                gear?.PropertyB is not null,
                trait?.Name ?? "Choose trait",
                trait?.Description ?? "",
                trait is not null,
                card.HasWeapon ? weapon?.Keywords ?? "" : "",
                card.HasWeapon ? icons.Weapon(gear?.WeaponId) : SlotIcon(card.Slot),
                icons.Trait(gear?.TraitId),
                ResourceLabel(weapon),
                !card.HasWeapon || weapon is not null);
        }
    }

    void RefreshTalents()
    {
        var career = book.Career;
        var enabled = book.Current is not null;
        for (var row = 0; row < 6; row++)
        {
            for (var column = 0; column < 3; column++)
            {
                var cell = talents[row, column];
                var talent = row < career.Talents.Count && column < career.Talents[row].Count
                    ? career.Talents[row][column]
                    : null;
                cell.Visible = talent is not null;
                cell.ShowTalent(
                    talent?.Name ?? "",
                    talent?.Description ?? "",
                    talent is null ? null : icons.Talent(career.Id, row, column),
                    book.Current is { } current && current.Talents[row] == column,
                    enabled && talent is not null,
                    descriptions.Checked);
                tips.SetToolTip(cell, talent?.Description ?? "");
            }
        }
    }

    void OpenPicker(string slot, string field, Rectangle screenAnchor)
    {
        noteToken = null;
        if (pickerSlot == slot && pickerField == field)
        {
            ClosePicker();
            return;
        }

        var choices = field == "loadout" ? LoadoutChoices() : Choices(slot, field);
        if (choices.Count == 0)
        {
            ClosePicker();
            return;
        }

        pickerSlot = slot;
        pickerField = field;
        pickerCaption.Text = field == "loadout" ? "Loadout" : PickerTitle(slot, field);
        pickerDetail.Text = "";
        pickerDetail.Visible = false;
        FillPicker(choices);
        PositionPopup(screenAnchor);
        pickerHost.Visible = true;
        pickerHost.BringToFront();
        HideHover();
    }

    void ClosePicker()
    {
        noteToken = null;
        pickerSlot = null;
        pickerField = null;
        pickerHost.Visible = false;
        pickerDetail.Text = "";
    }

    void ApplyPick(PickChoice choice)
    {
        var slot = pickerSlot;
        var field = pickerField;
        if (slot is null || field is null)
            return;

        switch (field)
        {
            case "loadout":
                book.SelectLoadout(choice.Id);
                ClosePicker();
                RefreshBoard();
                return;
            case "weapon":
                book.SetWeapon(slot, choice.Id);
                break;
            case "trait":
                book.SetTrait(slot, choice.Id);
                break;
            case "property-a":
                book.SetProperty(slot, 0, choice.Id);
                break;
            case "property-b":
                book.SetProperty(slot, 1, choice.Id);
                break;
        }

        ClosePicker();
        RefreshCards();
    }

    void ApplyNote(PickChoice choice)
    {
        if (noteToken is null)
            return;

        var token = noteToken;
        notesEditor.Replace(token.Start, token.Length, choice.Label);
    }

    void UpdateNoteSuggest()
    {
        if (Suppressed || pickerSlot is not null && pickerSlot != "note")
            return;

        var token = NoteMarkup.TokenAt(notesEditor.Notes, notesEditor.Caret);
        if (token is null)
        {
            if (pickerSlot == "note")
                ClosePicker();
            return;
        }

        var choices = Suggest(token);
        if (choices.Count == 0)
        {
            if (pickerSlot == "note")
                ClosePicker();
            return;
        }

        ShowNoteChoices(token, choices);
    }

    void ShowNoteChoices(NoteToken token, List<PickChoice> choices)
    {
        noteToken = token;
        pickerSlot = "note";
        pickerField = token.Part;
        pickerCaption.Text = token.Part switch
        {
            "weapon" => "Weapons",
            "trait" => "Traits",
            _ => "Properties"
        };
        pickerDetail.Text = "";
        pickerDetail.Visible = false;
        FillPicker(choices);
        PositionPopup(notesEditor.CaretScreenRect());
        pickerHost.Visible = true;
        pickerHost.BringToFront();
        HideHover();
    }

    void FillPicker(List<PickChoice> choices)
    {
        suppress++;
        try
        {
            picker.Items.Clear();
            foreach (var choice in choices)
                picker.Items.Add(choice);
            if (picker.Items.Count > 0)
                picker.TopIndex = 0;
        }
        finally
        {
            suppress--;
        }
    }

    List<PickChoice> LoadoutChoices()
    {
        var list = new List<PickChoice>();
        foreach (var loadout in book.ForCurrentCareer())
            list.Add(new PickChoice(loadout.Id, DisplayName(loadout), null));
        return list;
    }

    void PositionPopup(Rectangle screenAnchor)
    {
        pickerAnchor = screenAnchor;
        var anchor = RectangleToClient(screenAnchor);
        var limits = ClientRectangle;
        limits.Inflate(-8, -8);
        var rows = Math.Min(8, Math.Max(1, picker.Items.Count));
        const int captionHeight = 24;
        var width = Math.Clamp(MeasurePopupWidth(), 160, Math.Max(160, limits.Width - 16));
        var detailHeight = DetailHeight(width);
        var maxDetail = Math.Max(picker.ItemHeight, limits.Height - captionHeight - picker.ItemHeight - 8);
        detailHeight = Math.Min(detailHeight, maxDetail);
        pickerDetail.Visible = detailHeight > 0;
        var chrome = 2 + captionHeight + detailHeight;
        var below = limits.Bottom - (anchor.Bottom + 2);
        var above = anchor.Top - 2 - limits.Top;
        var room = Math.Max(picker.ItemHeight + chrome, Math.Max(below, above));
        while (rows > 1 && chrome + rows * picker.ItemHeight > room)
            rows--;
        if (chrome + rows * picker.ItemHeight > limits.Height)
            rows = Math.Max(1, (limits.Height - chrome) / picker.ItemHeight);

        var listHeight = rows * picker.ItemHeight;
        var height = chrome + listHeight;
        var x = Math.Clamp(anchor.Left, limits.Left, Math.Max(limits.Left, limits.Right - width));
        var y = anchor.Bottom + 2;
        if (below < height && above > below)
            y = anchor.Top - height - 2;
        y = Math.Clamp(y, limits.Top, Math.Max(limits.Top, limits.Bottom - height));

        var inner = width - 2;
        var showBar = picker.Items.Count > rows;
        var barWidth = showBar ? 14 : 0;
        pickerBar.Visible = showBar;
        pickerCaption.SetBounds(1, 1, inner, captionHeight);
        picker.SetBounds(1, 1 + captionHeight, inner - barWidth, listHeight);
        pickerBar.SetBounds(picker.Right, picker.Top, barWidth, listHeight);
        pickerDetail.SetBounds(1, picker.Bottom, inner, detailHeight);
        pickerHost.SetBounds(x, y, width, height);
        SyncPickerBar();
    }

    int DetailHeight(int width)
    {
        if (pickerDetail.Text.Length == 0)
            return 0;

        var textWidth = Math.Max(40, width - 2 - pickerDetail.Padding.Horizontal);
        var size = TextRenderer.MeasureText(
            pickerDetail.Text,
            pickerDetail.Font,
            new Size(textWidth, int.MaxValue),
            TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
        return size.Height + pickerDetail.Padding.Vertical;
    }

    int MeasurePopupWidth()
    {
        var width = 160;
        foreach (PickChoice choice in picker.Items)
        {
            var text = TextRenderer.MeasureText(choice.Label, Font);
            width = Math.Max(width, text.Width + 48);
        }

        return width;
    }

    void SyncPickerBar()
    {
        if (pickerSync)
            return;

        pickerSync = true;
        pickerBar.SetRange(Math.Max(1, picker.Items.Count), picker.VisibleRows, picker.TopIndex);
        pickerSync = false;
    }

    List<PickChoice> Choices(string slot, string field)
    {
        var list = new List<PickChoice>();
        if (field == "weapon")
        {
            var ids = slot == "primary" ? book.Career.Primary : book.Career.Secondary;
            foreach (var weapon in book.Catalog.Weapons(ids))
                list.Add(new PickChoice(weapon.Id, weapon.Name, weapon.Keywords));
            return list;
        }

        if (field == "trait")
        {
            foreach (var trait in book.Catalog.Traits(book.TraitGroup(slot)))
                list.Add(new PickChoice(trait.Id, trait.Name, trait.Description));
            return list;
        }

        var gear = book.Gear(slot);
        var blocked = field == "property-a" ? gear?.PropertyB : gear?.PropertyA;
        foreach (var property in book.Catalog.Properties(book.PropertyPool(slot)))
        {
            if (property.Id == blocked)
                continue;
            list.Add(new PickChoice(property.Id, property.Line, null));
        }

        return list;
    }

    List<PickChoice> Suggest(NoteToken token)
    {
        var list = new List<PickChoice>();
        var weapons = CareerWeapons();
        if (token.Part == "weapon")
        {
            foreach (var match in Rank(weapons, token.Query, static item => item.Name))
                list.Add(new PickChoice(match.Id, match.Name, match.Keywords));
            return list;
        }

        var weapon = weapons.FirstOrDefault(item =>
            item.Name.Equals(token.Span.Weapon, StringComparison.OrdinalIgnoreCase));
        if (weapon is null)
            return list;

        if (token.Part == "trait")
        {
            foreach (var trait in Rank(book.Catalog.Traits(weapon.Traits), token.Query, static item => item.Name))
                list.Add(new PickChoice(trait.Id, trait.Name, trait.Description));
            return list;
        }

        var used = new HashSet<string>(
            token.Span.Properties.Where(name => !name.Equals(token.Query, StringComparison.OrdinalIgnoreCase)),
            StringComparer.OrdinalIgnoreCase);
        var properties = book.Catalog.Properties(weapon.Properties)
            .Where(item => !used.Contains(item.Name))
            .Where(item => token.Query.Length == 0
                || item.Name.Contains(token.Query, StringComparison.OrdinalIgnoreCase)
                || item.Line.Contains(token.Query, StringComparison.OrdinalIgnoreCase));
        foreach (var property in properties
            .OrderBy(item => item.Name.StartsWith(token.Query, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
            list.Add(new PickChoice(property.Id, property.Name, property.Line));
        return list;
    }

    bool TryResolve(NoteSpan span, out ResolvedNote resolved)
    {
        resolved = null!;
        var weapon = CareerWeapons().FirstOrDefault(item =>
            item.Name.Equals(span.Weapon, StringComparison.OrdinalIgnoreCase));
        if (weapon is null)
            return false;

        var pool = book.Catalog.Properties(weapon.Properties);
        var matched = new List<PropertyInfo>();
        foreach (var name in span.Properties)
        {
            var property = pool.FirstOrDefault(item =>
                item.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
                || item.Line.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (property is null || matched.Any(item => item.Id == property.Id))
                continue;
            matched.Add(property);
            if (matched.Count == 2)
                break;
        }

        var trait = book.Catalog.Traits(weapon.Traits).FirstOrDefault(item =>
            item.Name.Equals(span.Trait, StringComparison.OrdinalIgnoreCase));
        resolved = new ResolvedNote(
            weapon,
            matched.Count > 0 ? matched[0] : null,
            matched.Count > 1 ? matched[1] : null,
            trait);
        return true;
    }

    IEnumerable<WeaponInfo> CareerWeapons()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in book.Career.Primary.Concat(book.Career.Secondary))
        {
            if (!seen.Add(id))
                continue;
            var weapon = book.Catalog.FindWeapon(id);
            if (weapon is not null)
                yield return weapon;
        }
    }

    static IEnumerable<T> Rank<T>(IEnumerable<T> source, string query, Func<T, string> name) =>
        source
            .Where(item => query.Length == 0 || name(item).Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => name(item).StartsWith(query, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(item => name(item), StringComparer.OrdinalIgnoreCase);

    void FlushNotes()
    {
        noteSave.Stop();
        book.FlushNotes();
    }

    void ShowHover(int index)
    {
        if (pickerHost.Visible)
        {
            HideHover();
            return;
        }

        var span = NoteMarkup.SpanAt(notesEditor.Notes, index);
        if (span is null || !TryResolve(span, out var resolved))
        {
            HideHover();
            return;
        }

        if (hoverPop.Visible && hoverStart == span.Start)
            return;

        hoverStart = span.Start;
        hoverCard.SetSubtitle(resolved.Weapon.Traits is "ammo" or "heat" or "energy" ? "Ranged" : "Melee");
        hoverCard.ShowItem(
            true,
            resolved.Weapon.Name,
            resolved.A?.Line ?? "",
            resolved.B?.Line ?? "",
            resolved.A is not null,
            resolved.B is not null,
            resolved.Trait?.Name ?? "",
            resolved.Trait?.Description ?? "",
            resolved.Trait is not null,
            resolved.Weapon.Keywords,
            icons.Weapon(resolved.Weapon.Id),
            icons.Trait(resolved.Trait?.Id),
            ResourceLabel(resolved.Weapon),
            true);
        var cursor = Cursor.Position;
        var screen = Screen.FromPoint(cursor).WorkingArea;
        var x = cursor.X + 18;
        var y = cursor.Y + 18;
        if (x + hoverPop.Width > screen.Right)
            x = Math.Max(screen.Left, cursor.X - hoverPop.Width - 12);
        if (y + hoverPop.Height > screen.Bottom)
            y = Math.Max(screen.Top, cursor.Y - hoverPop.Height - 12);
        hoverPop.Location = new Point(x, y);
        if (!hoverPop.Visible)
            hoverPop.Show(this);
    }

    void HideHover()
    {
        hoverStart = -1;
        if (hoverPop.Visible)
            hoverPop.Hide();
    }

    static string PickerTitle(string slot, string field)
    {
        var name = slot switch
        {
            "primary" => "Primary",
            "secondary" => "Secondary",
            "necklace" => "Necklace",
            "charm" => "Charm",
            "trinket" => "Trinket",
            _ => slot
        };
        var kind = field switch
        {
            "weapon" => "weapon",
            "trait" => "trait",
            _ => "property"
        };
        return name + " " + kind;
    }

    void LayoutBoard()
    {
        if (!ready || layingOut)
            return;

        var view = board.ViewSize;
        if (view.Width < 80 || view.Height < 80)
            return;
        if (laidOutReady && laidOutSize == view && laidOutDescriptions == descriptions.Checked)
            return;

        layingOut = true;
        try
        {
            ClosePicker();
            HideHover();
            var keep = boardBar.Value;
            var height = Place(view.Width, view.Height);
            board.ShowDocument(view.Width, height, keep);
            laidOutSize = view;
            laidOutDescriptions = descriptions.Checked;
            laidOutReady = true;
        }
        finally
        {
            layingOut = false;
        }
    }

    int Place(int width, int viewHeight)
    {
        const int gap = 8;
        const int pad = 12;
        var inner = Math.Max(320, width - pad * 2);
        var x = pad;
        var y = pad;

        var shownCareers = new List<Control>(careerButtons.Length);
        for (var index = 0; index < careerButtons.Length; index++)
        {
            if (careerShown[index])
                shownCareers.Add(careerButtons[index]);
        }

        PlaceRow(heroButtons, x, y, inner, 72);
        y += 80;
        PlaceRow(shownCareers.ToArray(), x, y, inner, 84);
        y += 92;

        var buttonWidth = 88;
        var buttonsWidth = buttonWidth * 3 + gap * 2;
        var loadoutWidth = Math.Min(260, Math.Max(160, (inner - buttonsWidth) / 3));
        var nameWidth = Math.Max(80, inner - buttonsWidth - loadoutWidth - gap * 2);
        var barHeight = 44;
        loadoutButton.SetBounds(x, y, loadoutWidth, barHeight);
        nameFrame.SetBounds(loadoutButton.Right + gap, y, nameWidth, barHeight);
        var nameHeight = Math.Max(nameBox.Font.Height + 4, nameBox.PreferredHeight);
        var nameTop = Math.Max(2, (barHeight - nameHeight) / 2);
        nameBox.SetBounds(8, nameTop, Math.Max(1, nameWidth - 16), nameHeight);
        createButton.SetBounds(nameFrame.Right + gap, y, buttonWidth, barHeight);
        deleteButton.SetBounds(createButton.Right + gap, y, buttonWidth, barHeight);
        CloseButton.SetBounds(deleteButton.Right + gap, y, buttonWidth, barHeight);
        y += barHeight + 10;

        var cardHeight = Math.Clamp(viewHeight * 28 / 100, 260, 340);
        var cardWidth = (inner - gap * (cards.Length - 1)) / cards.Length;
        for (var index = 0; index < cards.Length; index++)
            cards[index].SetBounds(x + index * (cardWidth + gap), y, cardWidth, cardHeight);
        y += cardHeight + gap;

        var levelWidth = 64;
        var titleWidth = 180;
        var gridWidth = inner - levelWidth - gap;
        talentsCaption.SetBounds(x + levelWidth + Math.Max(0, (gridWidth - titleWidth) / 2), y, titleWidth, 24);
        var descriptionWidth = Math.Max(descriptions.Width, descriptions.PreferredSize.Width);
        descriptions.Location = new Point(Math.Max(x, x + inner - descriptionWidth), y + 2);
        descriptions.BringToFront();
        y += 28;
        var rowHeight = descriptions.Checked ? 88 : 72;
        var cellWidth = (inner - levelWidth - gap * 3) / 3;
        for (var row = 0; row < 6; row++)
        {
            levels[row].SetBounds(x, y, levelWidth, rowHeight);
            for (var column = 0; column < 3; column++)
            {
                talents[row, column].SetBounds(
                    x + levelWidth + gap + column * (cellWidth + gap),
                    y,
                    cellWidth,
                    rowHeight);
            }

            y += rowHeight + gap;
        }

        var notesWidth = Math.Max(240, inner * 3 / 5);
        var notesX = x + Math.Max(0, (inner - notesWidth) / 2);
        notesCaption.SetBounds(notesX, y, notesWidth, 22);
        y += 24;
        var notesHeight = Math.Max(360, viewHeight * 28 / 100);
        notesEditor.SetBounds(notesX, y, notesWidth, notesHeight);
        return notesEditor.Bottom + pad;
    }

    static void PlaceRow(Control[] controls, int x, int y, int inner, int height)
    {
        if (controls.Length == 0)
            return;

        const int gap = 8;
        var width = (inner - gap * (controls.Length - 1)) / controls.Length;
        foreach (var control in controls)
        {
            control.SetBounds(x, y, width, height);
            x += width + gap;
        }
    }

    RedItemCard MakeCard(string slot, string title, bool hasWeapon)
    {
        var card = new RedItemCard(slot, title, hasWeapon);
        card.PartClicked += (field, part) =>
        {
            var anchor = content.RectangleToScreen(new Rectangle(card.Left + part.X, card.Top + part.Y, part.Width, part.Height));
            OpenPicker(slot, field, anchor);
        };
        content.Controls.Add(card);
        return card;
    }

    CrestButton MakeButton(string text)
    {
        var button = new CrestButton
        {
            Text = text,
            Font = buttonFont
        };
        return button;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        AcceptMouse();
    }

    protected override void WndProc(ref Message m)
    {
        const int WM_MOUSEACTIVATE = 0x0021;
        const int WM_MOUSEWHEEL = 0x020A;
        const int MA_ACTIVATE = 1;
        if (m.Msg == WM_MOUSEWHEEL)
        {
            var packed = unchecked((int)(nint)m.WParam);
            RouteWheel((short)((packed >> 16) & 0xFFFF));
            return;
        }

        if (m.Msg == WM_MOUSEACTIVATE)
        {
            m.Result = (IntPtr)MA_ACTIVATE;
            return;
        }

        base.WndProc(ref m);
    }

    void RouteWheel(int delta)
    {
        var cursor = Cursor.Position;
        if (pickerHost.Visible && pickerHost.RectangleToScreen(pickerHost.ClientRectangle).Contains(cursor))
        {
            picker.Wheel(delta);
            return;
        }

        if (notesEditor.RectangleToScreen(notesEditor.ClientRectangle).Contains(cursor))
        {
            if (!notesEditor.Wheel(delta))
                board.Bar.Nudge(delta < 0 ? 64 : -64);
            return;
        }

        board.Bar.Nudge(delta < 0 ? 64 : -64);
    }

    void DrawPick(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= picker.Items.Count || picker.Items[e.Index] is not PickChoice choice)
            return;

        var selected = (e.State & DrawItemState.Selected) != 0 || e.Index == picker.SelectedIndex;
        using (var brush = new SolidBrush(selected ? KitLook.PickSelected : Paper))
            e.Graphics.FillRectangle(brush, e.Bounds);
        if (selected)
        {
            using var mark = new SolidBrush(KitLook.ButtonChosenBorder);
            e.Graphics.FillRectangle(mark, e.Bounds.Left, e.Bounds.Top, 3, e.Bounds.Height);
        }

        var image = pickerField switch
        {
            "weapon" => icons.Weapon(choice.Id),
            "trait" => icons.Trait(choice.Id),
            "loadout" => icons.Career(book.Career.Id, 22),
            _ => null
        };
        var textX = e.Bounds.Left + 10;
        if (image is not null)
        {
            var imageY = e.Bounds.Top + Math.Max(0, (e.Bounds.Height - image.Height) / 2);
            e.Graphics.DrawImage(image, e.Bounds.Left + 8, imageY, image.Width, image.Height);
            textX += image.Width + 8;
        }

        TextRenderer.DrawText(
            e.Graphics,
            choice.Label,
            Font,
            new Rectangle(textX, e.Bounds.Top, Math.Max(1, e.Bounds.Right - textX - 6), e.Bounds.Height),
            selected ? KitLook.PickSelectedText : Ink,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
    }

    Image? SlotIcon(string slot)
    {
        if (slot == "necklace")
            return icons.Slot("necklace");
        if (slot == "charm")
            return icons.Slot("charm");
        if (slot == "trinket")
            return icons.Slot("trinket");
        if (slot == "secondary" && book.Career.Id is not ("grail-knight" or "warrior-priest-of-sigmar" or "slayer"))
            return icons.Slot("ranged");
        return icons.Slot("melee");
    }

    static string DisplayName(Loadout loadout) =>
        string.IsNullOrWhiteSpace(loadout.Name) ? "Loadout" : loadout.Name;

    static string ResourceLabel(WeaponInfo? weapon) => weapon?.Traits switch
    {
        "ammo" => "Ammunition",
        "heat" => "Overcharge",
        "energy" => "Energy",
        _ => ""
    };

    static Image? LoadImage(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Catalog", "icons", fileName);
        if (!File.Exists(path))
            return null;

        try
        {
            using var stream = File.OpenRead(path);
            using var source = new Bitmap(stream);
            return new Bitmap(source);
        }
        catch (Exception)
        {
            return null;
        }
    }

    void PaintScene(Graphics graphics, Rectangle destination)
    {
        if (backdrop is null || destination.Width < 1 || destination.Height < 1)
        {
            graphics.Clear(KitLook.Shade);
            return;
        }

        var view = board.ViewSize;
        var frameWidth = view.Width > 1 ? view.Width : destination.Width;
        var frameHeight = view.Height > 1 ? view.Height : Math.Max(1, frameWidth * 9 / 16);
        var scale = Math.Max(frameWidth / (float)backdrop.Width, frameHeight / (float)backdrop.Height);
        var width = backdrop.Width * scale;
        var height = backdrop.Height * scale;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.DrawImage(
            backdrop,
            destination.X + (destination.Width - width) / 2f,
            destination.Y,
            width,
            height);
    }

    static string NoteToken(WeaponInfo weapon, PropertyInfo? first, PropertyInfo? second, TraitInfo? trait)
    {
        var text = weapon.Name;
        var properties = new List<string>(2);
        if (first is not null)
            properties.Add(first.Name);
        if (second is not null)
            properties.Add(second.Name);
        if (properties.Count > 0 || trait is not null)
        {
            text += ": " + string.Join(", ", properties);
            if (trait is not null)
                text += "; " + trait.Name;
        }

        return "[" + text + "]";
    }

    sealed record PickChoice(string Id, string Label, string? Detail)
    {
        public override string ToString() => Label;
    }

    sealed record ResolvedNote(WeaponInfo Weapon, PropertyInfo? A, PropertyInfo? B, TraitInfo? Trait);

    sealed class CardPop : Form
    {
        public CardPop(Control card)
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            ShowIcon = false;
            StartPosition = FormStartPosition.Manual;
            ControlBox = false;
            TopMost = true;
            ClientSize = new Size(300, 270);
            Controls.Add(card);
            card.Dock = DockStyle.Fill;
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                var parameters = base.CreateParams;
                parameters.ExStyle |= 0x00000080 | 0x00000008 | 0x08000000;
                return parameters;
            }
        }
    }
}
