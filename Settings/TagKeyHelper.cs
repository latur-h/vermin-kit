using Poss.Win.Automation.Common.Keys.Enums;
using Poss.Win.Automation.Input;

namespace VerminKit;

static class TagKeyHelper
{
    static readonly string[] KeyNames = Enum.GetNames<VirtualKey>()
        .Where(static name => name is not ("None" or "CapsLock" or "NumLock" or "ScrollLock"))
        .ToArray();

    public static bool TryNormalize(string? input, out string key)
    {
        key = "";
        if (string.IsNullOrWhiteSpace(input))
            return false;

        if (!InputSimulator.TryParse(input.Trim(), out var stroke))
            return false;

        key = stroke.Key.ToString().Trim();
        return key.Length > 0;
    }

    public static string Down(string key) => key + " Down";

    public static string Up(string key) => key + " Up";

    public static List<string> KeysDown(InputSimulator simulator)
    {
        var down = new List<string>();
        foreach (var name in KeyNames)
        {
            if (simulator.GetKeyState(name))
                down.Add(name);
        }

        return down;
    }

    public static string? Pick(IReadOnlyList<string> down)
    {
        string? modifier = null;
        foreach (var name in down)
        {
            if (!TryNormalize(name, out _))
                continue;

            if (IsModifier(name))
            {
                modifier ??= name;
                continue;
            }

            return name;
        }

        return modifier;
    }

    static bool IsModifier(string name) =>
        name.Contains("Shift", StringComparison.Ordinal)
        || name.Contains("Control", StringComparison.Ordinal)
        || name.Contains("Menu", StringComparison.Ordinal)
        || name.Contains("Alt", StringComparison.Ordinal)
        || name is "Win" or "LWin" or "RWin";
}
