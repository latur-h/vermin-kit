namespace VerminKit;

sealed class TagSettings
{
    public const int MinDelayMs = 10;
    public const int MaxDelayMs = 60_000;
    public const string EditKey = "F5";
    public const string NotesKey = "F6";
    public const string DefaultActivateKey = "F1";
    public const string DefaultDeactivateKey = "F2";

    readonly object gate = new();
    string key;
    int tagDelayMs;
    int keyGapMs;
    int? offsetX;
    int? offsetY;
    string activateKey;
    string deactivateKey;
    string? legacyAnchor;

    public TagSettings()
    {
        var loaded = TagConfigStore.Load();
        key = loaded.Snapshot.Key;
        tagDelayMs = loaded.Snapshot.TagDelayMs;
        keyGapMs = loaded.Snapshot.KeyGapMs;
        offsetX = loaded.Snapshot.OffsetX;
        offsetY = loaded.Snapshot.OffsetY;
        activateKey = loaded.Snapshot.ActivateKey;
        deactivateKey = loaded.Snapshot.DeactivateKey;
        legacyAnchor = loaded.LegacyAnchor;
    }

    public TagSnapshot Snapshot()
    {
        lock (gate)
            return new TagSnapshot(key, tagDelayMs, keyGapMs, offsetX, offsetY, activateKey, deactivateKey);
    }

    public Point ResolveStatus(WindowBounds window, Size size)
    {
        lock (gate)
        {
            if (offsetX is int x && offsetY is int y)
                return OverlayPlacement.AtOffset(window, size, x, y);
            if (OverlayPlacement.TryPlaceLegacy(legacyAnchor, window, size, out var legacy))
                return legacy;
            return OverlayPlacement.TopCenter(window, size);
        }
    }

    public bool TrySetKey(string? input)
    {
        if (!TagKeyHelper.TryNormalize(input, out var normalized))
            return false;

        if (IsReservedKey(normalized))
            return false;

        lock (gate)
            key = normalized;
        Save();
        return true;
    }

    public bool TrySetActivateKey(string? input) => TrySetHotkey(input, activate: true);

    public bool TrySetDeactivateKey(string? input) => TrySetHotkey(input, activate: false);

    bool TrySetHotkey(string? input, bool activate)
    {
        if (!TagKeyHelper.TryNormalize(input, out var normalized) || IsReservedKey(normalized))
            return false;

        lock (gate)
        {
            var other = activate ? deactivateKey : activateKey;
            if (normalized.Equals(other, StringComparison.OrdinalIgnoreCase))
                return false;

            if (activate)
                activateKey = normalized;
            else
                deactivateKey = normalized;
        }

        Save();
        return true;
    }

    public static bool IsReservedKey(string key) =>
        key.Equals(EditKey, StringComparison.OrdinalIgnoreCase)
        || key.Equals(NotesKey, StringComparison.OrdinalIgnoreCase);

    public void SetTagDelayMs(int milliseconds)
    {
        lock (gate)
            tagDelayMs = ClampDelay(milliseconds);
        Save();
    }

    public void SetKeyGapMs(int milliseconds)
    {
        lock (gate)
            keyGapMs = ClampDelay(milliseconds);
        Save();
    }

    public void SetOffset(int x, int y)
    {
        lock (gate)
        {
            offsetX = x;
            offsetY = y;
            legacyAnchor = null;
        }
        Save();
    }

    void Save()
    {
        string? legacy;
        lock (gate)
            legacy = offsetX is null ? legacyAnchor : null;
        TagConfigStore.Save(Snapshot(), legacy);
    }

    public static int ClampDelay(int milliseconds) =>
        Math.Clamp(milliseconds, MinDelayMs, MaxDelayMs);
}

readonly record struct TagSnapshot(
    string Key,
    int TagDelayMs,
    int KeyGapMs,
    int? OffsetX,
    int? OffsetY,
    string ActivateKey,
    string DeactivateKey);
