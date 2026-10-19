using System.Globalization;

namespace Rask.DevTools.Panel;

/// <summary>
///     Typing letters in the tree jumps to the next row whose name starts with them — the WAI-ARIA type-ahead.
/// </summary>
/// <remarks>
///     The same letter pressed again cycles through the rows starting with it; a growing prefix searches from the row the
///     cursor is on, so refining "Ta" to "Tas" does not skip the match it just found. No timer: each letter compares the
///     time since the one before it.
/// </remarks>
internal struct DevToolsTypeAhead
{
    /// <summary>How long a typed prefix lives.</summary>
    internal static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(500);

    private string? _buffer;
    private long _at;

    /// <summary>The row to move to for <paramref name="key" />, or <c>-1</c> when nothing matches.</summary>
    internal int Next(string key, int cursor, IReadOnlyList<string> texts, TimeProvider clock)
    {
        if (texts.Count == 0)
        {
            return -1;
        }

        var now = clock.GetTimestamp();
        var buffer = _buffer is null || clock.GetElapsedTime(_at, now) > Timeout ? key : _buffer + key;
        _buffer = buffer;
        _at = now;

        // "aaa" is somebody pressing `a` three times to reach the third row starting with `a`.
        var needle = buffer.All(c => c == buffer[0]) ? key : buffer;
        var start = needle.Length == 1 ? cursor + 1 : cursor;
        for (var i = 0; i < texts.Count; i++)
        {
            var at = (start + i) % texts.Count;
            if (texts[at].StartsWith(needle, true, CultureInfo.CurrentCulture))
            {
                return at;
            }
        }

        return -1;
    }
}
