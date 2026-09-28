using System.Text.Json;

namespace VerminKit;

static class LoadoutStore
{
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static string FilePath { get; } = Path.Combine(TagConfigStore.DirectoryPath, "notes.json");

    public static NotesFile Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return new NotesFile();

            return JsonSerializer.Deserialize<NotesFile>(File.ReadAllText(FilePath), JsonOptions) ?? new NotesFile();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new NotesFile();
        }
    }

    public static void Save(NotesFile file)
    {
        try
        {
            Directory.CreateDirectory(TagConfigStore.DirectoryPath);
            var tempPath = FilePath + ".tmp";
            File.WriteAllText(tempPath, JsonSerializer.Serialize(file, JsonOptions));
            File.Move(tempPath, FilePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}

sealed class NotesFile
{
    public string? CareerId { get; set; }
    public bool ShowDescriptions { get; set; }
    public Dictionary<string, string>? Selected { get; set; }
    public List<Loadout>? Loadouts { get; set; }
}

sealed class Loadout
{
    public string Id { get; set; } = "";
    public string CareerId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Notes { get; set; } = "";
    public GearChoice Primary { get; set; } = new();
    public GearChoice Secondary { get; set; } = new();
    public GearChoice Necklace { get; set; } = new();
    public GearChoice Charm { get; set; } = new();
    public GearChoice Trinket { get; set; } = new();
    public int?[] Talents { get; set; } = new int?[6];
}

sealed class GearChoice
{
    public string? WeaponId { get; set; }
    public string? TraitId { get; set; }
    public string? PropertyA { get; set; }
    public string? PropertyB { get; set; }
}
