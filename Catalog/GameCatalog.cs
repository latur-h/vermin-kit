using System.Text.Json;

namespace VerminKit;

sealed class GameCatalog
{
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    readonly Dictionary<string, HeroInfo> heroes;
    readonly Dictionary<string, CareerInfo> careers;
    readonly Dictionary<string, WeaponInfo> weapons;
    readonly Dictionary<string, IReadOnlyList<TraitInfo>> traits;
    readonly Dictionary<string, IReadOnlyList<PropertyInfo>> properties;

    GameCatalog(
        IReadOnlyList<HeroInfo> heroList,
        Dictionary<string, HeroInfo> heroes,
        Dictionary<string, CareerInfo> careers,
        Dictionary<string, WeaponInfo> weapons,
        Dictionary<string, IReadOnlyList<TraitInfo>> traits,
        Dictionary<string, IReadOnlyList<PropertyInfo>> properties)
    {
        Heroes = heroList;
        this.heroes = heroes;
        this.careers = careers;
        this.weapons = weapons;
        this.traits = traits;
        this.properties = properties;
    }

    public IReadOnlyList<HeroInfo> Heroes { get; }

    public static GameCatalog Load()
    {
        using var stream = typeof(GameCatalog).Assembly.GetManifestResourceStream("VerminKit.Catalog.catalog.json")
            ?? throw new InvalidOperationException("Missing career catalog.");
        var file = JsonSerializer.Deserialize<CatalogFile>(stream, JsonOptions)
            ?? throw new InvalidOperationException("Career catalog is empty.");

        var heroList = new List<HeroInfo>();
        var heroes = new Dictionary<string, HeroInfo>(StringComparer.Ordinal);
        var careers = new Dictionary<string, CareerInfo>(StringComparer.Ordinal);
        foreach (var heroFile in file.Heroes ?? [])
        {
            if (string.IsNullOrWhiteSpace(heroFile.Id))
                continue;

            var careerList = new List<CareerInfo>();
            foreach (var careerFile in heroFile.Careers ?? [])
            {
                if (string.IsNullOrWhiteSpace(careerFile.Id))
                    continue;

                var talentRows = new List<IReadOnlyList<TalentInfo>>();
                foreach (var row in careerFile.Talents ?? [])
                {
                    talentRows.Add(row.Select(static talent => new TalentInfo(
                        talent.Name ?? "",
                        talent.Description ?? "")).ToArray());
                }

                var career = new CareerInfo(
                    careerFile.Id,
                    careerFile.Name ?? careerFile.Id,
                    heroFile.Id,
                    heroFile.Name ?? heroFile.Id,
                    careerFile.Primary ?? [],
                    careerFile.Secondary ?? [],
                    talentRows);
                careerList.Add(career);
                careers[career.Id] = career;
            }

            var hero = new HeroInfo(heroFile.Id, heroFile.Name ?? heroFile.Id, careerList);
            heroList.Add(hero);
            heroes[hero.Id] = hero;
        }

        var weapons = new Dictionary<string, WeaponInfo>(StringComparer.Ordinal);
        foreach (var weapon in file.Weapons ?? [])
        {
            if (string.IsNullOrWhiteSpace(weapon.Id))
                continue;
            weapons[weapon.Id] = new WeaponInfo(
                weapon.Id,
                weapon.Name ?? weapon.Id,
                weapon.Keywords ?? "",
                weapon.Traits ?? "",
                weapon.Properties ?? "");
        }

        return new GameCatalog(
            heroList,
            heroes,
            careers,
            weapons,
            MapTraits(file.Traits),
            MapProperties(file.Properties));
    }

    public HeroInfo? FindHero(string? id) =>
        id is not null && heroes.TryGetValue(id, out var hero) ? hero : null;

    public CareerInfo? FindCareer(string? id) =>
        id is not null && careers.TryGetValue(id, out var career) ? career : null;

    public WeaponInfo? FindWeapon(string? id) =>
        id is not null && weapons.TryGetValue(id, out var weapon) ? weapon : null;

    public IReadOnlyList<WeaponInfo> Weapons(IReadOnlyList<string> ids)
    {
        var list = new List<WeaponInfo>(ids.Count);
        foreach (var id in ids)
        {
            if (weapons.TryGetValue(id, out var weapon))
                list.Add(weapon);
        }

        return list;
    }

    public IReadOnlyList<TraitInfo> Traits(string? group) =>
        group is not null && traits.TryGetValue(group, out var list) ? list : [];

    public IReadOnlyList<PropertyInfo> Properties(string? pool) =>
        pool is not null && properties.TryGetValue(pool, out var list) ? list : [];

    public TraitInfo? FindTrait(string? group, string? id)
    {
        if (id is null)
            return null;
        foreach (var trait in Traits(group))
        {
            if (trait.Id.Equals(id, StringComparison.Ordinal))
                return trait;
        }

        return null;
    }

    public PropertyInfo? FindProperty(string? pool, string? id)
    {
        if (id is null)
            return null;
        foreach (var property in Properties(pool))
        {
            if (property.Id.Equals(id, StringComparison.Ordinal))
                return property;
        }

        return null;
    }

    static Dictionary<string, IReadOnlyList<TraitInfo>> MapTraits(Dictionary<string, List<TraitFile>>? source)
    {
        var map = new Dictionary<string, IReadOnlyList<TraitInfo>>(StringComparer.Ordinal);
        if (source is null)
            return map;

        foreach (var (group, items) in source)
        {
            map[group] = items
                .Where(static item => !string.IsNullOrWhiteSpace(item.Id))
                .Select(static item => new TraitInfo(item.Id!, item.Name ?? item.Id!, item.Description ?? ""))
                .ToArray();
        }

        return map;
    }

    static Dictionary<string, IReadOnlyList<PropertyInfo>> MapProperties(Dictionary<string, List<PropertyFile>>? source)
    {
        var map = new Dictionary<string, IReadOnlyList<PropertyInfo>>(StringComparer.Ordinal);
        if (source is null)
            return map;

        foreach (var (pool, items) in source)
        {
            map[pool] = items
                .Where(static item => !string.IsNullOrWhiteSpace(item.Id))
                .Select(static item => new PropertyInfo(item.Id!, item.Name ?? item.Id!, item.Line ?? item.Name ?? item.Id!))
                .ToArray();
        }

        return map;
    }

    sealed class CatalogFile
    {
        public List<HeroFile>? Heroes { get; set; }
        public List<WeaponFile>? Weapons { get; set; }
        public Dictionary<string, List<TraitFile>>? Traits { get; set; }
        public Dictionary<string, List<PropertyFile>>? Properties { get; set; }
    }

    sealed class HeroFile
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public List<CareerFile>? Careers { get; set; }
    }

    sealed class CareerFile
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public List<string>? Primary { get; set; }
        public List<string>? Secondary { get; set; }
        public List<List<TalentFile>>? Talents { get; set; }
    }

    sealed class TalentFile
    {
        public string? Name { get; set; }
        public string? Description { get; set; }
    }

    sealed class WeaponFile
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public string? Keywords { get; set; }
        public string? Traits { get; set; }
        public string? Properties { get; set; }
    }

    sealed class TraitFile
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public string? Description { get; set; }
    }

    sealed class PropertyFile
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public string? Line { get; set; }
    }
}

sealed record HeroInfo(string Id, string Name, IReadOnlyList<CareerInfo> Careers);

sealed record CareerInfo(
    string Id,
    string Name,
    string HeroId,
    string HeroName,
    IReadOnlyList<string> Primary,
    IReadOnlyList<string> Secondary,
    IReadOnlyList<IReadOnlyList<TalentInfo>> Talents);

sealed record TalentInfo(string Name, string Description);

sealed record WeaponInfo(string Id, string Name, string Keywords, string Traits, string Properties);

sealed record TraitInfo(string Id, string Name, string Description);

sealed record PropertyInfo(string Id, string Name, string Line);
