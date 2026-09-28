using System.Drawing.Drawing2D;

namespace VerminKit;

sealed class NotesOverlayForm : OverlayForm
{
    static readonly Color PanelColor = Color.FromArgb(28, 28, 28);
    static readonly Color ButtonColor = Color.FromArgb(48, 48, 48);
    static readonly Color SelectedColor = Color.FromArgb(92, 48, 18);
    static readonly Color SelectedBorder = Color.FromArgb(196, 122, 48);
    static readonly Color IdleBorder = Color.FromArgb(70, 70, 70);
    static readonly Color HintColor = Color.FromArgb(180, 180, 180);
    static readonly int[] TalentLevels = [5, 10, 15, 20, 25, 30];

    readonly LoadoutBook book;
    readonly IconCatalog icons = new();
    readonly bool[] careerShown = new bool[4];
    readonly ToolTip tips = new() { InitialDelay = 300, AutoPopDelay = 30000, ShowAlways = true };
    readonly Image? backdrop;
    readonly Panel scroll = new() { Dock = DockStyle.Fill, AutoScroll = false };
    readonly Button[] heroButtons;
    readonly Button[] careerButtons;
    readonly Button loadoutButton;
    readonly TextBox nameBox = new() { MaxLength = 80, BorderStyle = BorderStyle.FixedSingle };
    readonly TextBox notesBox = new()
    {
        Multiline = true,
        AcceptsReturn = true,
        ScrollBars = ScrollBars.Vertical,
        MaxLength = 4000,
        BorderStyle = BorderStyle.FixedSingle
    };
    readonly Button createButton;
    readonly Button deleteButton;
    readonly CheckBox descriptions = new() { Text = "Show descriptions", AutoSize = true };
    readonly RedItemCard[] cards;
    readonly Button[,] talents = new Button[6, 3];
    readonly Label[] levels = new Label[6];
    readonly Label pickerCaption = new() { AutoSize = false, ForeColor = HintColor };
    readonly ListBox picker = new()
    {
        IntegralHeight = false,
        BorderStyle = BorderStyle.FixedSingle,
        DrawMode = DrawMode.OwnerDrawFixed,
        ItemHeight = 28
    };
    readonly Label pickerDetail = new() { AutoSize = false, ForeColor = HintColor };
    readonly Label talentsCaption = new() { Text = "Talents", AutoSize = false };
    readonly Label notesCaption = new() { Text = "Play notes", AutoSize = false };

    string? pickerSlot;
    string? pickerField;
    int suppress;
    bool layingOut;
    bool ready;
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
        backdrop = LoadBackdrop();
        RedItemCard.Backdrop = backdrop;
        scroll.BackColor = Color.FromArgb(18, 12, 8);
        scroll.Paint += PaintBackdrop;
        scroll.MouseDown += (_, _) => ClosePicker();
        Controls.Add(scroll);

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
            SetIcon(heroButtons[index], icons.Hero(heroId));
            scroll.Controls.Add(heroButtons[index]);
        }

        careerButtons = new Button[4];
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
            scroll.Controls.Add(careerButtons[index]);
        }

        nameBox.TextChanged += (_, _) =>
        {
            if (Suppressed)
                return;
            book.Rename(nameBox.Text);
            RefreshLoadoutNames();
        };
        notesBox.TextChanged += (_, _) =>
        {
            if (Suppressed)
                return;
            book.SetNotes(notesBox.Text);
        };
        nameBox.Enter += (_, _) => ClosePicker();
        notesBox.Enter += (_, _) => ClosePicker();
        createButton = MakeButton("New");
        createButton.TextAlign = ContentAlignment.MiddleCenter;
        createButton.Click += (_, _) =>
        {
            book.Create();
            ClosePicker();
            RefreshBoard();
            nameBox.Focus();
            nameBox.SelectAll();
        };
        deleteButton = MakeButton("Delete");
        deleteButton.TextAlign = ContentAlignment.MiddleCenter;
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
        loadoutButton.Click += (_, _) => OpenPicker("loadout", "loadout", loadoutButton.Bounds);
        var closeButton = MakeButton("Close");
        closeButton.TextAlign = ContentAlignment.MiddleCenter;
        closeButton.Click += (_, _) => CloseRequested?.Invoke();
        scroll.Controls.Add(loadoutButton);
        scroll.Controls.Add(nameBox);
        scroll.Controls.Add(createButton);
        scroll.Controls.Add(deleteButton);
        scroll.Controls.Add(closeButton);
        CloseButton = closeButton;

        cards =
        [
            MakeCard("primary", "Primary", true),
            MakeCard("secondary", "Secondary", true),
            MakeCard("necklace", "Necklace", false),
            MakeCard("charm", "Charm", false),
            MakeCard("trinket", "Trinket", false)
        ];

        descriptions.ForeColor = Color.FromArgb(120, 170, 220);
        descriptions.CheckedChanged += (_, _) =>
        {
            RefreshTalents();
            LayoutBoard();
        };
        scroll.Controls.Add(descriptions);
        scroll.Controls.Add(talentsCaption);
        scroll.Controls.Add(notesCaption);
        scroll.Controls.Add(notesBox);
        StyleField(nameBox);
        StyleField(notesBox);
        picker.BackColor = Color.FromArgb(22, 22, 22);
        picker.ForeColor = Color.FromArgb(235, 235, 235);
        pickerCaption.BackColor = PanelColor;
        pickerDetail.BackColor = PanelColor;
        talentsCaption.BackColor = PanelColor;
        notesCaption.BackColor = PanelColor;
        descriptions.BackColor = PanelColor;
        picker.MouseUp += (_, args) =>
        {
            if (Suppressed)
                return;
            var index = picker.IndexFromPoint(args.Location);
            if (index < 0 || picker.Items[index] is not PickChoice choice)
                return;
            ApplyPick(choice);
        };
        picker.DrawItem += DrawPick;
        picker.MouseMove += (_, args) =>
        {
            var index = picker.IndexFromPoint(args.Location);
            pickerDetail.Text = index >= 0 && picker.Items[index] is PickChoice choice
                ? choice.Detail ?? ""
                : "";
        };
        scroll.Controls.Add(pickerCaption);
        scroll.Controls.Add(picker);
        scroll.Controls.Add(pickerDetail);

        for (var row = 0; row < 6; row++)
        {
            levels[row] = new Label
            {
                Text = TalentLevels[row].ToString(),
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.FromArgb(212, 168, 92),
                BackColor = PanelColor
            };
            scroll.Controls.Add(levels[row]);
            for (var column = 0; column < 3; column++)
            {
                var button = MakeButton("");
                button.TextAlign = ContentAlignment.TopLeft;
                var talentRow = row;
                var talentColumn = column;
                button.Click += (_, _) =>
                {
                    ClosePicker();
                    book.ToggleTalent(talentRow, talentColumn);
                    RefreshTalents();
                };
                talents[row, column] = button;
                scroll.Controls.Add(button);
            }
        }

        RefreshBoard();
        var area = Screen.PrimaryScreen?.WorkingArea.Size ?? new Size(1280, 720);
        Size = area;
        CreateHandle();
        ready = true;
        LayoutBoard();
    }

    Button CloseButton { get; }

    bool Suppressed => suppress > 0;

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
                PaintSelected(heroButtons[index], book.Catalog.Heroes[index].Id == book.Career.HeroId);

            for (var index = 0; index < careerButtons.Length; index++)
            {
                var career = hero is not null && index < hero.Careers.Count ? hero.Careers[index] : null;
                careerShown[index] = career is not null;
                careerButtons[index].Visible = careerShown[index];
                careerButtons[index].Text = career?.Name ?? "";
                SetIcon(careerButtons[index], career is null ? null : icons.Career(career.Id));
                PaintSelected(careerButtons[index], career?.Id == book.Career.Id);
            }

            loadoutButton.Text = book.Current is null ? "Loadout" : DisplayName(book.Current);

            var hasLoadout = book.Current is not null;
            nameBox.Enabled = hasLoadout;
            notesBox.Enabled = hasLoadout;
            deleteButton.Enabled = hasLoadout;
            descriptions.Enabled = hasLoadout;
            nameBox.Text = book.Current?.Name ?? "";
            notesBox.Text = book.Current?.Notes ?? "";
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
            var lineA = book.Catalog.FindProperty(pool, gear?.PropertyA)?.Line ?? "Choose property";
            var lineB = book.Catalog.FindProperty(pool, gear?.PropertyB)?.Line ?? "Choose property";
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
                var button = talents[row, column];
                var talent = row < career.Talents.Count && column < career.Talents[row].Count
                    ? career.Talents[row][column]
                    : null;
                button.Visible = talent is not null;
                button.Enabled = enabled && talent is not null;
                button.Text = talent is null
                    ? ""
                    : descriptions.Checked
                        ? talent.Name + "\n" + talent.Description
                        : talent.Name;
                SetIcon(button, talent is null ? null : icons.Talent(career.Id, row, column));
                tips.SetToolTip(button, talent?.Description ?? "");
                PaintSelected(button, book.Current is { } current && current.Talents[row] == column);
            }
        }
    }

    void OpenPicker(string slot, string field, Rectangle anchor)
    {
        if (pickerSlot == slot && pickerField == field)
        {
            ClosePicker();
            return;
        }

        var choices = field == "loadout" ? LoadoutChoices() : Choices(slot, field);
        if (choices.Count == 0)
            return;

        pickerSlot = slot;
        pickerField = field;
        pickerCaption.Text = field == "loadout" ? "Loadout" : PickerTitle(slot, field);
        pickerDetail.Text = "";
        suppress++;
        try
        {
            picker.Items.Clear();
            foreach (var choice in choices)
                picker.Items.Add(choice);
        }
        finally
        {
            suppress--;
        }

        PositionPopup(anchor);
        pickerCaption.Visible = true;
        picker.Visible = true;
        pickerDetail.Visible = field is "weapon" or "trait";
        pickerCaption.BringToFront();
        picker.BringToFront();
        pickerDetail.BringToFront();
    }

    void ClosePicker()
    {
        pickerSlot = null;
        pickerField = null;
        pickerCaption.Visible = false;
        picker.Visible = false;
        pickerDetail.Visible = false;
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

    List<PickChoice> LoadoutChoices()
    {
        var list = new List<PickChoice>();
        foreach (var loadout in book.ForCurrentCareer())
            list.Add(new PickChoice(loadout.Id, DisplayName(loadout), null));
        return list;
    }

    void PositionPopup(Rectangle anchor)
    {
        var width = Math.Clamp(Math.Max(anchor.Width, 280), 220, Math.Max(220, scroll.ClientSize.Width - 16));
        var listHeight = Math.Min(8 * picker.ItemHeight, Math.Max(picker.ItemHeight, picker.Items.Count * picker.ItemHeight));
        var detailHeight = pickerField is "weapon" or "trait" ? 48 : 0;
        const int captionHeight = 20;
        var height = captionHeight + listHeight + detailHeight;
        var x = Math.Clamp(anchor.Left, 8, Math.Max(8, scroll.ClientSize.Width - width - 8));
        var y = anchor.Bottom + 2;
        if (y + height > scroll.ClientSize.Height - 8)
            y = Math.Max(8, anchor.Top - height - 2);

        pickerCaption.SetBounds(x, y, width, captionHeight);
        picker.SetBounds(x, y + captionHeight, width, listHeight);
        pickerDetail.SetBounds(x, picker.Bottom, width, detailHeight);
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
        if (!ready || layingOut || ClientSize.Width < 80 || ClientSize.Height < 80)
            return;
        if (laidOutReady && laidOutSize == ClientSize && laidOutDescriptions == descriptions.Checked)
            return;

        layingOut = true;
        try
        {
            ClosePicker();
            Place(ClientSize.Width, ClientSize.Height);
            laidOutSize = ClientSize;
            laidOutDescriptions = descriptions.Checked;
            laidOutReady = true;
        }
        finally
        {
            layingOut = false;
        }
    }

    void Place(int width, int height)
    {
        const int gap = 8;
        const int pad = 12;
        var inner = Math.Max(320, width - pad * 2);
        var x = pad;
        var y = pad;
        var bottom = height - pad;

        var shownCareers = new List<Control>(careerButtons.Length);
        for (var index = 0; index < careerButtons.Length; index++)
        {
            if (careerShown[index])
                shownCareers.Add(careerButtons[index]);
        }

        PlaceRow(heroButtons, x, y, inner, 40);
        y += 48;
        PlaceRow(shownCareers.ToArray(), x, y, inner, 48);
        y += 56;

        var buttonWidth = 72;
        var buttonsWidth = buttonWidth * 3 + gap * 2;
        var loadoutWidth = Math.Min(220, Math.Max(120, (inner - buttonsWidth) / 3));
        var nameWidth = Math.Max(80, inner - buttonsWidth - loadoutWidth - gap * 2);
        loadoutButton.SetBounds(x, y, loadoutWidth, 28);
        nameBox.SetBounds(loadoutButton.Right + gap, y, nameWidth, 28);
        createButton.SetBounds(nameBox.Right + gap, y, buttonWidth, 28);
        deleteButton.SetBounds(createButton.Right + gap, y, buttonWidth, 28);
        CloseButton.SetBounds(deleteButton.Right + gap, y, buttonWidth, 28);
        y += 38;

        var notesHeight = descriptions.Checked ? 72 : 110;
        var reserved = 20 + notesHeight + 8 + 26;
        var available = Math.Max(220, bottom - y - reserved);
        var cardHeight = Math.Clamp(available * 50 / 100, 176, 320);
        var talentBand = available - cardHeight - gap;
        var rowHeight = Math.Max(28, (talentBand - gap * 5) / 6);
        if (descriptions.Checked)
            rowHeight = Math.Max(rowHeight, 48);

        var cardWidth = (inner - gap * (cards.Length - 1)) / cards.Length;
        for (var index = 0; index < cards.Length; index++)
            cards[index].SetBounds(x + index * (cardWidth + gap), y, cardWidth, cardHeight);
        y += cardHeight + gap;

        talentsCaption.SetBounds(x, y, 80, 22);
        var descriptionWidth = Math.Max(descriptions.Width, descriptions.PreferredSize.Width);
        descriptions.Location = new Point(Math.Max(x, x + inner - descriptionWidth), y);
        y += 26;

        var levelWidth = 36;
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

        notesCaption.SetBounds(x, y, inner, 18);
        y += 20;
        notesBox.SetBounds(x, y, inner, Math.Max(48, bottom - y));
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
            var anchor = new Rectangle(card.Left + part.X, card.Top + part.Y, part.Width, part.Height);
            OpenPicker(slot, field, anchor);
        };
        scroll.Controls.Add(card);
        return card;
    }

    Button MakeButton(string text)
    {
        var button = new BoardButton
        {
            Text = text,
            FlatStyle = FlatStyle.Flat,
            ForeColor = Color.FromArgb(235, 235, 235),
            BackColor = ButtonColor,
            Cursor = Cursors.Hand,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(6, 2, 6, 2)
        };
        button.FlatAppearance.BorderColor = IdleBorder;
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(64, 64, 64);
        button.FlatAppearance.MouseDownBackColor = Color.FromArgb(84, 84, 84);
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
        const int MA_ACTIVATE = 1;
        if (m.Msg == WM_MOUSEACTIVATE)
        {
            m.Result = (IntPtr)MA_ACTIVATE;
            return;
        }

        base.WndProc(ref m);
    }

    void DrawPick(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= picker.Items.Count || picker.Items[e.Index] is not PickChoice choice)
            return;

        var selected = (e.State & DrawItemState.Selected) != 0;
        using var brush = new SolidBrush(selected ? SelectedColor : picker.BackColor);
        e.Graphics.FillRectangle(brush, e.Bounds);

        var image = pickerField == "weapon"
            ? icons.Weapon(choice.Id)
            : pickerField == "trait" ? icons.Trait(choice.Id) : null;
        var textX = e.Bounds.Left + 6;
        if (image is not null)
        {
            var imageY = e.Bounds.Top + Math.Max(0, (e.Bounds.Height - image.Height) / 2);
            e.Graphics.DrawImage(image, e.Bounds.Left + 4, imageY, image.Width, image.Height);
            textX += image.Width + 6;
        }

        TextRenderer.DrawText(
            e.Graphics,
            choice.Label,
            Font,
            new Rectangle(textX, e.Bounds.Top, Math.Max(1, e.Bounds.Right - textX), e.Bounds.Height),
            ForeColor,
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

    static void SetIcon(Button button, Image? image)
    {
        button.Image = image;
        if (image is null)
            return;

        button.ImageAlign = ContentAlignment.MiddleLeft;
        button.TextImageRelation = TextImageRelation.ImageBeforeText;
    }

    static void PaintSelected(Button button, bool selected)
    {
        button.BackColor = selected ? SelectedColor : ButtonColor;
        button.FlatAppearance.BorderColor = selected ? SelectedBorder : IdleBorder;
    }

    static void StyleField(TextBox box)
    {
        box.BackColor = Color.FromArgb(22, 22, 22);
        box.ForeColor = Color.FromArgb(235, 235, 235);
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

    static Image? LoadBackdrop()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Catalog", "icons", "background.png");
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

    void PaintBackdrop(object? sender, PaintEventArgs e)
    {
        if (backdrop is null)
            return;

        var bounds = scroll.ClientRectangle;
        if (bounds.Width < 1 || bounds.Height < 1)
            return;

        var scale = Math.Max(bounds.Width / (float)backdrop.Width, bounds.Height / (float)backdrop.Height);
        var width = backdrop.Width * scale;
        var height = backdrop.Height * scale;
        e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        e.Graphics.DrawImage(backdrop, (bounds.Width - width) / 2f, (bounds.Height - height) / 2f, width, height);
    }

    sealed record PickChoice(string Id, string Label, string? Detail)
    {
        public override string ToString() => Label;
    }
}
