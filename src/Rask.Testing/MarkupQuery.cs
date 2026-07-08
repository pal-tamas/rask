namespace Rask.Testing;

// Minimal, dependency-free attribute lookup over rendered HTML — enough to pull handler ids and check
// attributes without pulling in an HTML parser. Rendered Rask markup uses double-quoted attributes, so a
// substring scan is exact. Public surface is RenderedComponent.Attr; kept internal here.
internal static class MarkupQuery
{
    // The value of the first name="..." attribute in html, or null if absent.
    public static string? Attr(string html, string name)
    {
        var marker = name + "=\"";
        var from = 0;
        while (true)
        {
            var i = html.IndexOf(marker, from, StringComparison.Ordinal);
            if (i < 0)
            {
                return null;
            }

            // Require an attribute boundary before the name — start of string or ASCII whitespace — so a
            // short name (e.g. "label") doesn't match inside a longer one ("aria-label", "data-label").
            var before = i == 0 ? ' ' : html[i - 1];
            if (before is ' ' or '\t' or '\n' or '\r' or '\f')
            {
                var start = i + marker.Length;
                var end = html.IndexOf('"', start);
                return end < 0 ? null : html.Substring(start, end - start);
            }

            from = i + marker.Length;
        }
    }
}
