using System.Text.Json;

namespace VerminKit;

static class TagConfigStore
{
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static string DirectoryPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Vermin Kit");

    public static string FilePath { get; } = Path.Combine(DirectoryPath, "config.json");

    public static LoadedConfig Load()
    {
        var snapshot = new TagSnapshot(
            "XButton1",
            300,
            10,
            null,
            null,
            TagSettings.DefaultActivateKey,
            TagSettings.DefaultDeactivateKey);
        if (!TagKeyHelper.TryNormalize(snapshot.Key, out var defaultKey))
            defaultKey = snapshot.Key;
        snapshot = snapshot with { Key = defaultKey };
        string? legacyAnchor = null;

        try
        {
            Directory.CreateDirectory(DirectoryPath);
            var legacyFile = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Vermintide 2",
                "config.json");
            var source = File.Exists(FilePath) ? FilePath : legacyFile;
            if (!File.Exists(source))
            {
                Save(snapshot);
                return new LoadedConfig(snapshot, null);
            }

            var file = JsonSerializer.Deserialize<TagConfigFile>(File.ReadAllText(source), JsonOptions);
            if (file is null)
                return new LoadedConfig(snapshot, null);

            if (TagKeyHelper.TryNormalize(file.Key, out var key))
                snapshot = snapshot with { Key = key };
            if (file.TagDelayMs is int tagDelay)
                snapshot = snapshot with { TagDelayMs = TagSettings.ClampDelay(tagDelay) };
            if (file.KeyGapMs is int keyGap)
                snapshot = snapshot with { KeyGapMs = TagSettings.ClampDelay(keyGap) };
            if (file.X is int x && file.Y is int y)
                snapshot = snapshot with { OffsetX = x, OffsetY = y };
            else if (!string.IsNullOrWhiteSpace(file.Anchor))
                legacyAnchor = file.Anchor;

            var activate = NormalizeHotkey(file.ActivateKey, TagSettings.DefaultActivateKey);
            var deactivate = NormalizeHotkey(file.DeactivateKey, TagSettings.DefaultDeactivateKey);
            if (activate.Equals(deactivate, StringComparison.OrdinalIgnoreCase))
                deactivate = activate.Equals(TagSettings.DefaultDeactivateKey, StringComparison.OrdinalIgnoreCase)
                    ? TagSettings.DefaultActivateKey
                    : TagSettings.DefaultDeactivateKey;
            snapshot = snapshot with { ActivateKey = activate, DeactivateKey = deactivate };
            if (!source.Equals(FilePath, StringComparison.OrdinalIgnoreCase))
                Save(snapshot, legacyAnchor);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
        }

        return new LoadedConfig(snapshot, legacyAnchor);
    }

    public static void Save(TagSnapshot snapshot, string? legacyAnchor = null)
    {
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            var file = new TagConfigFile
            {
                Key = snapshot.Key,
                TagDelayMs = snapshot.TagDelayMs,
                KeyGapMs = snapshot.KeyGapMs,
                X = snapshot.OffsetX,
                Y = snapshot.OffsetY,
                ActivateKey = snapshot.ActivateKey,
                DeactivateKey = snapshot.DeactivateKey,
                Anchor = snapshot.OffsetX is null ? legacyAnchor : null
            };
            var tempPath = FilePath + ".tmp";
            File.WriteAllText(tempPath, JsonSerializer.Serialize(file, JsonOptions));
            File.Move(tempPath, FilePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    sealed class TagConfigFile
    {
        public string? Key { get; set; }
        public int? TagDelayMs { get; set; }
        public int? KeyGapMs { get; set; }
        public int? X { get; set; }
        public int? Y { get; set; }
        public string? ActivateKey { get; set; }
        public string? DeactivateKey { get; set; }
        public string? Anchor { get; set; }
    }

    static string NormalizeHotkey(string? input, string fallback)
    {
        if (!TagKeyHelper.TryNormalize(input, out var key) || TagSettings.IsReservedKey(key))
            return fallback;
        return key;
    }
}

readonly record struct LoadedConfig(TagSnapshot Snapshot, string? LegacyAnchor);
