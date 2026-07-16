namespace Rask.Testing;

/// <summary>
///     Attribute lookups over rendered Rask markup. <see cref="RenderedComponent" /> exposes these over its
///     own <see cref="RenderedComponent.Html" />; use this class directly for HTML you hold as a string —
///     for example markup pulled out of a live payload, or captured from an earlier render.
/// </summary>
/// <remarks>
///     Deliberately a substring scan rather than an HTML parser: Rask's serializer always emits
///     double-quoted attributes and encodes <c>&lt;</c>, <c>&gt;</c> and <c>"</c> in values, so the closing
///     quote is an exact delimiter and no dependency is needed. It reads attributes, not structure — it has
///     no notion of elements or nesting, so it cannot tell you which element a match came from.
/// </remarks>
public static class Markup
{
    /// <summary>
    ///     The value of the first <c>{name}="…"</c> attribute in <paramref name="html" />, or <c>null</c> if
    ///     absent.
    /// </summary>
    public static string? Attr(string html, string name)
    {
        ArgumentNullException.ThrowIfNull(html);
        ArgumentNullException.ThrowIfNull(name);

        foreach (var value in Scan(html, name))
        {
            return value;
        }

        return null;
    }

    /// <summary>
    ///     The value of every <c>{name}="…"</c> attribute in <paramref name="html" />, in document order —
    ///     index this when several elements carry the same attribute. Empty if none match.
    /// </summary>
    public static IReadOnlyList<string> Attrs(string html, string name)
    {
        ArgumentNullException.ThrowIfNull(html);
        ArgumentNullException.ThrowIfNull(name);

        List<string> values = [];
        foreach (var value in Scan(html, name))
        {
            values.Add(value);
        }

        return values;
    }

    private static IEnumerable<string> Scan(string html, string name)
    {
        var marker = name + "=\"";
        var from = 0;
        while (true)
        {
            var i = html.IndexOf(marker, from, StringComparison.Ordinal);
            if (i < 0)
            {
                yield break;
            }

            // Require an attribute boundary before the name — start of string or ASCII whitespace — so a
            // short name (e.g. "label") doesn't match inside a longer one ("aria-label", "data-label").
            var before = i == 0 ? ' ' : html[i - 1];
            if (before is ' ' or '\t' or '\n' or '\r' or '\f')
            {
                var start = i + marker.Length;
                var end = html.IndexOf('"', start);
                if (end < 0)
                {
                    // An unterminated value means the rest of the string can't yield a well-formed match.
                    yield break;
                }

                yield return html[start..end];
                from = end + 1;
            }
            else
            {
                from = i + marker.Length;
            }
        }
    }
}
