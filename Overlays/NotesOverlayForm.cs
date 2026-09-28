namespace VerminKit;

sealed class NotesOverlayForm : OverlayForm
{
    const int CardHeight = 176;

    static readonly Color PanelColor = Color.FromArgb(28, 28, 28);
    static readonly Color ButtonColor = Color.FromArgb(48, 48, 48);
    static readonly Color SelectedColor = Color.FromArgb(92, 48, 18);
    static readonly Color SelectedBorder = Color.FromArgb(196, 122, 48);
    static readonly Color IdleBorder = Color.FromArgb(70, 70, 70);
    static readonly Color HintColor = Color.FromArgb(180, 180, 180);
    static readonly int[] TalentLevels = [5, 10, 15, 20, 25, 30];

    readonly LoadoutBook book;
    readonly ToolTip tips = new() { InitialDelay = 300, AutoPopDelay = 30000, ShowAlways = true };
    readonly Panel scroll = new() { Dock = DockStyle.Fill, AutoScroll = true };
    readonly Button[] heroButtons;
    readonly Button[] careerButtons;
    readonly ComboBox loadouts = new() { DropDownStyle = ComboBoxStyle.DropDownList };
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
    readonly CardUi[] cards;
    readonly Button[,] talents = new Button[6, 3];
    readonly Label[] levels = new Label[6];
    readonly Label pickerCaption = new() { AutoSize = false, ForeColor = HintColor };
    readonly ListBox picker = new() { IntegralHeight = false, BorderStyle = BorderStyle.FixedSingle };
    readonly Label pickerDetail = new() { AutoSize = false, ForeColor = HintColor };
    readonly Label talentsCaption = new() { Text = "Talents", AutoSize = false };
    readonly Label notesCaption = new() { Text = "Play notes", AutoSize = false };

    string? pickerSlot;
    string? pickerField;
    int suppress;
    bool layingOut;
    bool ready;

    public event Action? CloseRequested;

    protected override bool AllowsActivation => true;

    public NotesOverlayForm(LoadoutBook book) : base()
    {
        this.book = book;
        Text = "Notes";
        scroll.BackColor = PanelColor;
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

        loadouts.SelectedIndexChanged += (_, _) =>
        {
            if (Suppressed || loadouts.SelectedItem is not LoadoutItem item)
                return;
            book.SelectLoadout(item.Id);
            ClosePicker();
            RefreshBoard();
        };
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
        var closeButton = MakeButton("Close");
        closeButton.TextAlign = ContentAlignment.MiddleCenter;
        closeButton.Click += (_, _) => CloseRequested?.Invoke();
        scroll.Controls.Add(loadouts);
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
        loadouts.BackColor = Color.FromArgb(22, 22, 22);
        loadouts.ForeColor = Color.FromArgb(235, 235, 235);
        picker.BackColor = Color.FromArgb(22, 22, 22);
        picker.ForeColor = Color.FromArgb(235, 235, 235);
        pickerCaption.BackColor = PanelColor;
        pickerDetail.BackColor = PanelColor;
        talentsCaption.BackColor = PanelColor;
        notesCaption.BackColor = PanelColor;
        descriptions.BackColor = PanelColor;
        picker.SelectedIndexChanged += (_, _) =>
        {
            if (Suppressed || picker.SelectedItem is not PickChoice choice)
                return;
            ApplyPick(choice);
        };
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
                    book.ToggleTalent(talentRow, talentColumn);
                    RefreshTalents();
                };
                talents[row, column] = button;
                scroll.Controls.Add(button);
            }
        }

        RefreshBoard();
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
                careerButtons[index].Visible = career is not null;
                careerButtons[index].Text = career?.Name ?? "";
                PaintSelected(careerButtons[index], career?.Id == book.Career.Id);
            }

            loadouts.Items.Clear();
            foreach (var loadout in book.ForCurrentCareer())
                loadouts.Items.Add(new LoadoutItem(loadout.Id, DisplayName(loadout)));
            if (book.Current is not null)
            {
                for (var index = 0; index < loadouts.Items.Count; index++)
                {
                    if (loadouts.Items[index] is LoadoutItem item && item.Id == book.Current.Id)
                        loadouts.SelectedIndex = index;
                }
            }

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
        if (book.Current is null)
            return;

        var selected = loadouts.SelectedIndex;
        if (selected < 0 || selected >= loadouts.Items.Count)
            return;

        suppress++;
        try
        {
            loadouts.Items[selected] = new LoadoutItem(book.Current.Id, DisplayName(book.Current));
        }
        finally
        {
            suppress--;
        }
    }

    void RefreshCards()
    {
        var enabled = book.Current is not null;
        foreach (var card in cards)
        {
            var gear = book.Gear(card.Slot);
            var weapon = book.Catalog.FindWeapon(gear?.WeaponId);
            if (card.HasWeapon)
            {
                card.Weapon.Text = weapon?.Name ?? "Choose weapon";
                card.Keywords.Text = weapon?.Keywords ?? "";
                card.Weapon.Enabled = enabled;
            }

            var trait = book.Catalog.FindTrait(book.TraitGroup(card.Slot), gear?.TraitId);
            card.Trait.Text = trait?.Name ?? "Choose trait";
            card.Trait.Enabled = enabled && book.TraitGroup(card.Slot) is not null;
            tips.SetToolTip(card.Trait, trait?.Description ?? "");

            var pool = book.PropertyPool(card.Slot);
            card.PropertyA.Text = book.Catalog.FindProperty(pool, gear?.PropertyA)?.Line ?? "Choose property";
            card.PropertyB.Text = book.Catalog.FindProperty(pool, gear?.PropertyB)?.Line ?? "Choose property";
            var canPickProperty = enabled && pool is not null;
            card.PropertyA.Enabled = canPickProperty;
            card.PropertyB.Enabled = canPickProperty;
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
                tips.SetToolTip(button, talent?.Description ?? "");
                PaintSelected(button, book.Current is { } current && current.Talents[row] == column);
            }
        }
    }

    void OpenPicker(string slot, string field)
    {
        if (pickerSlot == slot && pickerField == field)
        {
            ClosePicker();
            LayoutBoard();
            return;
        }

        var choices = Choices(slot, field);
        if (choices.Count == 0)
            return;

        pickerSlot = slot;
        pickerField = field;
        pickerCaption.Text = PickerTitle(slot, field);
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

        pickerCaption.Visible = true;
        picker.Visible = true;
        pickerDetail.Visible = true;
        LayoutBoard();
        scroll.ScrollControlIntoView(picker);
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
        LayoutBoard();
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

        layingOut = true;
        try
        {
            var width = ClientSize.Width;
            var bottom = Place(width);
            if (bottom > ClientSize.Height)
            {
                var narrower = width - SystemInformation.VerticalScrollBarWidth;
                if (narrower > 80)
                    bottom = Place(narrower);
            }

            scroll.AutoScrollMinSize = new Size(0, bottom);
        }
        finally
        {
            layingOut = false;
        }
    }

    int Place(int width)
    {
        const int gap = 8;
        var inner = Math.Max(320, width - 24);
        var x = 12;
        var y = 12;

        PlaceRow(heroButtons, x, y, inner, 34);
        y += 42;
        PlaceRow(careerButtons.Where(static button => button.Visible).ToArray(), x, y, inner, 40);
        y += 48;

        var buttonWidth = 72;
        var buttonsWidth = buttonWidth * 3 + gap * 2;
        var comboWidth = Math.Min(180, Math.Max(96, (inner - buttonsWidth) / 3));
        var nameWidth = Math.Max(80, inner - buttonsWidth - comboWidth - gap * 2);
        loadouts.SetBounds(x, y, comboWidth, 26);
        nameBox.SetBounds(loadouts.Right + gap, y, nameWidth, 26);
        createButton.SetBounds(nameBox.Right + gap, y, buttonWidth, 26);
        deleteButton.SetBounds(createButton.Right + gap, y, buttonWidth, 26);
        CloseButton.SetBounds(deleteButton.Right + gap, y, buttonWidth, 26);
        y += 36;

        var cardWidth = 210;
        var columns = Math.Max(1, (inner + gap) / (cardWidth + gap));
        columns = Math.Min(columns, cards.Length);
        cardWidth = (inner - gap * (columns - 1)) / columns;
        for (var index = 0; index < cards.Length; index++)
        {
            var column = index % columns;
            var row = index / columns;
            cards[index].Panel.SetBounds(x + column * (cardWidth + gap), y + row * (CardHeight + gap), cardWidth, CardHeight);
        }

        var cardRows = (cards.Length + columns - 1) / columns;
        y += cardRows * (CardHeight + gap);

        pickerCaption.Visible = pickerSlot is not null;
        picker.Visible = pickerSlot is not null;
        pickerDetail.Visible = pickerSlot is not null;
        if (pickerSlot is not null)
        {
            pickerCaption.SetBounds(x, y, inner, 18);
            y += 20;
            picker.SetBounds(x, y, inner, 132);
            y += 136;
            pickerDetail.SetBounds(x, y, inner, 36);
            y += 40;
        }

        talentsCaption.SetBounds(x, y, 80, 22);
        descriptions.Location = new Point(x + inner - descriptions.Width, y);
        y += 26;

        var levelWidth = 36;
        var cellWidth = (inner - levelWidth - gap * 3) / 3;
        for (var row = 0; row < 6; row++)
        {
            var cellHeight = 36;
            if (descriptions.Checked)
            {
                for (var column = 0; column < 3; column++)
                {
                    var measured = TextRenderer.MeasureText(
                        talents[row, column].Text,
                        Font,
                        new Size(Math.Max(40, cellWidth - 12), int.MaxValue),
                        TextFormatFlags.WordBreak);
                    cellHeight = Math.Max(cellHeight, measured.Height + 10);
                }
            }

            levels[row].SetBounds(x, y, levelWidth, cellHeight);
            for (var column = 0; column < 3; column++)
            {
                talents[row, column].SetBounds(
                    x + levelWidth + gap + column * (cellWidth + gap),
                    y,
                    cellWidth,
                    cellHeight);
            }

            y += cellHeight + gap;
        }

        y += 4;
        notesCaption.SetBounds(x, y, inner, 18);
        y += 22;
        notesBox.SetBounds(x, y, inner, 140);
        return y + 152;
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

    CardUi MakeCard(string slot, string title, bool hasWeapon)
    {
        var panel = new Panel { BackColor = Color.FromArgb(36, 32, 30), Height = CardHeight };
        var heading = new Label
        {
            Text = title,
            ForeColor = Color.FromArgb(212, 168, 92),
            BackColor = panel.BackColor,
            Location = new Point(8, 6),
            Size = new Size(180, 18),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        var weapon = MakeButton("Choose weapon");
        var keywords = new Label
        {
            ForeColor = HintColor,
            BackColor = panel.BackColor,
            Location = new Point(8, 54),
            Size = new Size(180, 32),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        var trait = MakeButton("Choose trait");
        var propertyA = MakeButton("Choose property");
        var propertyB = MakeButton("Choose property");
        panel.Controls.Add(heading);
        panel.Controls.Add(keywords);
        if (hasWeapon)
        {
            weapon.SetBounds(8, 26, 180, 26);
            trait.SetBounds(8, 88, 180, 26);
            propertyA.SetBounds(8, 116, 180, 26);
            propertyB.SetBounds(8, 144, 180, 26);
        }
        else
        {
            weapon.Visible = false;
            keywords.Visible = false;
            trait.SetBounds(8, 32, 180, 26);
            propertyA.SetBounds(8, 64, 180, 26);
            propertyB.SetBounds(8, 96, 180, 26);
        }

        foreach (var button in new[] { weapon, trait, propertyA, propertyB })
        {
            button.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            panel.Controls.Add(button);
        }

        weapon.Click += (_, _) => OpenPicker(slot, "weapon");
        trait.Click += (_, _) => OpenPicker(slot, "trait");
        propertyA.Click += (_, _) => OpenPicker(slot, "property-a");
        propertyB.Click += (_, _) => OpenPicker(slot, "property-b");
        scroll.Controls.Add(panel);
        return new CardUi(slot, hasWeapon, panel, weapon, keywords, trait, propertyA, propertyB);
    }

    Button MakeButton(string text)
    {
        var button = new Button
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

    sealed record CardUi(
        string Slot,
        bool HasWeapon,
        Panel Panel,
        Button Weapon,
        Label Keywords,
        Button Trait,
        Button PropertyA,
        Button PropertyB);

    sealed record LoadoutItem(string Id, string Name)
    {
        public override string ToString() => Name;
    }

    sealed record PickChoice(string Id, string Label, string? Detail)
    {
        public override string ToString() => Label;
    }
}
