using System.Globalization;

namespace Rask.Ui;

/// <summary>
///     Typing letters jumps to the next node whose text starts with them — the WAI-ARIA type-ahead, as a value.
/// </summary>
/// <remarks>
///     <para>
///         No timer: a buffer that expires needs to know when the last letter arrived, not to be told later. Each search
///         compares the elapsed time against the one before it, so nothing is scheduled and nothing has to be disposed.
///     </para>
///     <para>
///         Two rules the pattern asks for, and both are about what the typist meant. The same letter pressed again cycles
///         through the nodes starting with it rather than looking for a doubled letter; a growing prefix searches from the
///         node the cursor is already on, so refining "fi" to "fil" does not skip the match it just found.
///     </para>
/// </remarks>
internal struct UiTypeAhead
{
    /// <summary>How long a typed prefix lives. The pattern's usual value, and what a slow typist expects.</summary>
    internal static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(500);

    private string? _buffer;
    private long _at;

    /// <summary>
    ///     The index to move to for <paramref name="key" />, or <c>-1</c> when nothing matches.
    /// </summary>
    /// <param name="key">The character typed.</param>
    /// <param name="cursor">Where the cursor is now.</param>
    /// <param name="texts">The visible nodes' text, in the order they are rendered.</param>
    /// <param name="clock">The clock the prefix expires by.</param>
    internal int Next(string key, int cursor, IReadOnlyList<string?> texts, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(texts);
        ArgumentNullException.ThrowIfNull(clock);

        if (texts.Count == 0)
        {
            return -1;
        }

        var now = clock.GetTimestamp();
        _buffer = _buffer is null || clock.GetElapsedTime(_at, now) > Timeout ? key : _buffer + key;
        _at = now;

        // "aaa" is somebody pressing `a` three times to see the third node starting with `a`, not a node called "aaa".
        var repeated = true;
        foreach (var c in _buffer)
        {
            if (c != _buffer[0])
            {
                repeated = false;
                break;
            }
        }

        var needle = repeated ? key : _buffer;
        var start = needle.Length == 1 ? cursor + 1 : cursor;
        for (var i = 0; i < texts.Count; i++)
        {
            var at = ((start + i) % texts.Count + texts.Count) % texts.Count;
            // The visitor's culture: a handler runs inside it, and what counts as a match for "ö" is a local question.
            if (texts[at]?.StartsWith(needle, true, CultureInfo.CurrentCulture) == true)
            {
                return at;
            }
        }

        return -1;
    }
}
