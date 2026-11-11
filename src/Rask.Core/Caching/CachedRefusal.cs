namespace Rask.Core.Caching;

/// <summary>
///     Refuses to store markup that only works live: what a <see cref="Components.Cached" /> fragment keeps is served to
///     later visitors as plain HTML, where nothing would answer a click, fill a binding or find a reference.
/// </summary>
/// <remarks>
///     Read off the markup itself, so it catches what <c>RASK117</c> cannot see — a handler in a component the
///     fragment wraps, however deep. It runs once per stored fragment, never on a hit.
/// </remarks>
internal static class CachedRefusal
{
    // What the serializer writes for each thing a stored copy would carry dead, and how a reader would name it.
    private static readonly (string Marker, string What)[] _live =
    [
        ("data-rask-on-", "an event handler"),
        ("data-rask-bind-on", "a bound input"),
        ("data-rask-ref", "an element reference")
    ];

    /// <summary>Throws when <paramref name="html" /> carries anything live, or something under the fragment wrote to the head.</summary>
    internal static void ThrowIfLive(string name, string html, bool addedHead)
    {
        foreach (var (marker, what) in _live)
        {
            var at = html.IndexOf(marker, StringComparison.Ordinal);
            if (at >= 0)
            {
                throw new InvalidOperationException(
                    $"Cached.Name(\"{name}\") holds {what} ({AttributeAt(html, at)}). What it keeps is served to later "
                    + "visitors as plain HTML, where nothing would answer it. Move the interactive part outside "
                    + $"Cached.Name(\"{name}\")[ … ], next to it, and keep only static markup inside.");
            }
        }

        if (addedHead)
        {
            throw new InvalidOperationException(
                $"Cached.Name(\"{name}\") wraps a component that contributes to the page's head. A later visitor is "
                + "handed the stored markup with that component never mounted, so the head would lose it. Contribute "
                + "the head from a component outside the fragment.");
        }
    }

    // The attribute's name, as the developer would search their markup for it.
    private static string AttributeAt(string html, int at)
    {
        var end = html.IndexOfAny(['=', ' ', '>'], at);
        return end < 0 ? html[at..] : html[at..end];
    }
}
