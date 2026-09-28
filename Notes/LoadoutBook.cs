namespace VerminKit;

sealed class LoadoutBook
{
    const int MaxNameLength = 80;
    const int MaxNotesLength = 4000;

    readonly GameCatalog catalog;
    readonly List<Loadout> loadouts = [];
    readonly Dictionary<string, string> selected = new(StringComparer.Ordinal);

    public LoadoutBook(GameCatalog catalog)
    {
        this.catalog = catalog;
        var file = LoadoutStore.Load();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var loadout in file.Loadouts ?? [])
        {
            if (string.IsNullOrWhiteSpace(loadout.Id) || !seen.Add(loadout.Id))
                continue;
            if (catalog.FindCareer(loadout.CareerId) is null)
                continue;

            Normalize(loadout);
            loadouts.Add(loadout);
        }

        if (file.Selected is not null)
        {
            foreach (var (careerId, loadoutId) in file.Selected)
            {
                if (loadouts.Any(loadout => loadout.Id == loadoutId && loadout.CareerId == careerId))
                    selected[careerId] = loadoutId;
            }
        }

        ShowDescriptions = file.ShowDescriptions;
        var career = catalog.FindCareer(file.CareerId) ?? catalog.Heroes[0].Careers[0];
        Career = career;
        Current = FindSelected(career.Id);
        if (Current is null)
            Create();
    }

    public GameCatalog Catalog => catalog;

    public CareerInfo Career { get; private set; }

    public bool ShowDescriptions { get; private set; }

    public Loadout? Current { get; private set; }

    public IReadOnlyList<Loadout> ForCurrentCareer() =>
        loadouts.Where(loadout => loadout.CareerId == Career.Id).ToArray();

    public void SelectHero(string heroId)
    {
        var hero = catalog.FindHero(heroId);
        if (hero is null || hero.Careers.Count == 0)
            return;
        if (hero.Careers.Any(career => career.Id == Career.Id))
            return;

        SelectCareer(hero.Careers[0].Id);
    }

    public void SelectCareer(string careerId)
    {
        var career = catalog.FindCareer(careerId);
        if (career is null || career.Id == Career.Id)
            return;

        Career = career;
        Current = FindSelected(career.Id);
        if (Current is null)
        {
            Create();
            return;
        }

        Save();
    }

    public void SelectLoadout(string loadoutId)
    {
        var loadout = ForCurrentCareer().FirstOrDefault(item => item.Id == loadoutId);
        if (loadout is null || Current?.Id == loadout.Id)
            return;

        Current = loadout;
        selected[Career.Id] = loadout.Id;
        Save();
    }

    public void Create()
    {
        var count = ForCurrentCareer().Count + 1;
        var loadout = new Loadout
        {
            Id = Guid.NewGuid().ToString("N"),
            CareerId = Career.Id,
            Name = "Loadout " + count,
            Talents = new int?[6]
        };
        loadouts.Add(loadout);
        Current = loadout;
        selected[Career.Id] = loadout.Id;
        Save();
    }

    public void DeleteCurrent()
    {
        if (Current is null)
            return;

        loadouts.Remove(Current);
        selected.Remove(Career.Id);
        Current = ForCurrentCareer().FirstOrDefault();
        if (Current is not null)
            selected[Career.Id] = Current.Id;
        Save();
    }

    public void Rename(string name)
    {
        if (Current is null)
            return;

        if (name.Length > MaxNameLength)
            name = name[..MaxNameLength];
        if (Current.Name == name)
            return;

        Current.Name = name;
        Save();
    }

    public void SetShowDescriptions(bool show)
    {
        if (ShowDescriptions == show)
            return;

        ShowDescriptions = show;
        Save();
    }

    public void SetNotes(string notes)
    {
        if (Current is null)
            return;

        notes = notes.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        if (notes.Length > MaxNotesLength)
            notes = notes[..MaxNotesLength];
        if (Current.Notes == notes)
            return;

        Current.Notes = notes;
        Save();
    }

    public void SetWeapon(string slot, string weaponId)
    {
        if (Gear(slot) is not { } gear)
            return;

        var allowed = slot == "primary" ? Career.Primary : Career.Secondary;
        if (!allowed.Contains(weaponId, StringComparer.Ordinal))
            return;

        var weapon = catalog.FindWeapon(weaponId);
        var previous = catalog.FindWeapon(gear.WeaponId);
        if (weapon is null || gear.WeaponId == weapon.Id)
            return;

        gear.WeaponId = weapon.Id;
        if (previous is null || previous.Traits != weapon.Traits)
            gear.TraitId = null;
        if (previous is null || previous.Properties != weapon.Properties)
        {
            gear.PropertyA = null;
            gear.PropertyB = null;
        }

        Save();
    }

    public void SetTrait(string slot, string traitId)
    {
        if (Gear(slot) is not { } gear)
            return;
        if (catalog.FindTrait(TraitGroup(slot), traitId) is null || gear.TraitId == traitId)
            return;

        gear.TraitId = traitId;
        Save();
    }

    public void SetProperty(string slot, int index, string propertyId)
    {
        if (Gear(slot) is not { } gear || index is not (0 or 1))
            return;
        if (catalog.FindProperty(PropertyPool(slot), propertyId) is null)
            return;

        if (index == 0)
        {
            if (gear.PropertyA == propertyId)
                return;
            gear.PropertyA = propertyId;
            if (gear.PropertyB == propertyId)
                gear.PropertyB = null;
        }
        else
        {
            if (gear.PropertyA == propertyId || gear.PropertyB == propertyId)
                return;
            gear.PropertyB = propertyId;
        }

        Save();
    }

    public void ToggleTalent(int row, int column)
    {
        if (Current is null || row is < 0 or > 5 || column is < 0 or > 2)
            return;
        if (row >= Career.Talents.Count || column >= Career.Talents[row].Count)
            return;

        Current.Talents[row] = Current.Talents[row] == column ? null : column;
        Save();
    }

    public GearChoice? Gear(string slot)
    {
        if (Current is null)
            return null;

        return slot switch
        {
            "primary" => Current.Primary,
            "secondary" => Current.Secondary,
            "necklace" => Current.Necklace,
            "charm" => Current.Charm,
            "trinket" => Current.Trinket,
            _ => null
        };
    }

    public string? TraitGroup(string slot)
    {
        if (slot is "necklace" or "charm" or "trinket")
            return slot;

        return catalog.FindWeapon(Gear(slot)?.WeaponId)?.Traits;
    }

    public string? PropertyPool(string slot)
    {
        if (slot is "necklace" or "charm" or "trinket")
            return slot;

        return catalog.FindWeapon(Gear(slot)?.WeaponId)?.Properties;
    }

    Loadout? FindSelected(string careerId)
    {
        if (selected.TryGetValue(careerId, out var loadoutId))
        {
            var saved = loadouts.FirstOrDefault(loadout => loadout.Id == loadoutId && loadout.CareerId == careerId);
            if (saved is not null)
                return saved;
        }

        return loadouts.FirstOrDefault(loadout => loadout.CareerId == careerId);
    }

    void Normalize(Loadout loadout)
    {
        var career = catalog.FindCareer(loadout.CareerId)!;
        loadout.Name = loadout.Name.Length > MaxNameLength ? loadout.Name[..MaxNameLength] : loadout.Name;
        loadout.Notes = loadout.Notes.Length > MaxNotesLength ? loadout.Notes[..MaxNotesLength] : loadout.Notes;
        loadout.Primary ??= new GearChoice();
        loadout.Secondary ??= new GearChoice();
        loadout.Necklace ??= new GearChoice();
        loadout.Charm ??= new GearChoice();
        loadout.Trinket ??= new GearChoice();
        loadout.Talents ??= new int?[6];
        NormalizeGear(loadout.Primary, career.Primary);
        NormalizeGear(loadout.Secondary, career.Secondary);
        NormalizeJewelry(loadout.Necklace, "necklace");
        NormalizeJewelry(loadout.Charm, "charm");
        NormalizeJewelry(loadout.Trinket, "trinket");

        var talents = new int?[6];
        for (var row = 0; row < talents.Length && row < loadout.Talents.Length && row < career.Talents.Count; row++)
        {
            var column = loadout.Talents[row];
            if (column is >= 0 and <= 2 && column < career.Talents[row].Count)
                talents[row] = column;
        }

        loadout.Talents = talents;
    }

    void NormalizeGear(GearChoice gear, IReadOnlyList<string> allowed)
    {
        if (gear.WeaponId is null || !allowed.Contains(gear.WeaponId, StringComparer.Ordinal))
            gear.WeaponId = null;

        var weapon = catalog.FindWeapon(gear.WeaponId);
        if (weapon is null || catalog.FindTrait(weapon.Traits, gear.TraitId) is null)
            gear.TraitId = null;
        NormalizeProperties(gear, weapon?.Properties);
    }

    void NormalizeJewelry(GearChoice gear, string pool)
    {
        gear.WeaponId = null;
        if (catalog.FindTrait(pool, gear.TraitId) is null)
            gear.TraitId = null;
        NormalizeProperties(gear, pool);
    }

    void NormalizeProperties(GearChoice gear, string? pool)
    {
        if (catalog.FindProperty(pool, gear.PropertyA) is null)
            gear.PropertyA = null;
        if (catalog.FindProperty(pool, gear.PropertyB) is null || gear.PropertyB == gear.PropertyA)
            gear.PropertyB = null;
    }

    void Save()
    {
        LoadoutStore.Save(new NotesFile
        {
            CareerId = Career.Id,
            ShowDescriptions = ShowDescriptions,
            Selected = new Dictionary<string, string>(selected, StringComparer.Ordinal),
            Loadouts = loadouts
        });
    }
}
