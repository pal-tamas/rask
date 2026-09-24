namespace Rask;

/// <summary>
/// How a shortcut such as <c>"mod+k"</c> is written for a reader: <c>⌘K</c> on a Mac, <c>Ctrl K</c> elsewhere.
/// </summary>
/// <remarks>
/// The same grammar the runtime matches <c>data-rask-shortcut</c> with: modifiers <c>mod</c>, <c>ctrl</c>,
/// <c>alt</c>, <c>shift</c> and <c>meta</c>, joined to one key by <c>+</c>, case-insensitive.
/// </remarks>
internal static class UiShortcut
{
    internal static string Describe(string shortcut, bool mac)
    {
        var parts = shortcut.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var words = new List<string>(parts.Length);
        foreach (var part in parts)
        {
            words.Add(part.ToLowerInvariant() switch
            {
                "mod" => mac ? "⌘" : "Ctrl",
                "meta" => mac ? "⌘" : "Win",
                "ctrl" => mac ? "⌃" : "Ctrl",
                "alt" => mac ? "⌥" : "Alt",
                "shift" => mac ? "⇧" : "Shift",
                var key when key.Length == 1 => key.ToUpperInvariant(),
                var key => char.ToUpperInvariant(key[0]) + key[1..],
            });
        }

        // A Mac writes the symbols run together, as its menus do; everything else spaces the words.
        return string.Join(mac ? "" : " ", words);
    }
}
