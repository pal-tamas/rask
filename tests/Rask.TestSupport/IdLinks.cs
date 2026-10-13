using System.Globalization;
using System.Text.RegularExpressions;

namespace Rask.TestSupport;

/// <summary>
///     What joins one element of a rendered page to another by id — <c>aria-labelledby</c>, <c>for</c>,
///     <c>popovertarget</c> — checked as a browser would resolve it, and the page with everything that is
///     rightly its own response's taken out, so two responses to one request can be compared.
/// </summary>
public static partial class IdLinks
{
    /// <summary>
    ///     Every id the page declares twice, and every reference that names an id the page does not have.
    ///     Empty for a page whose links all hold.
    /// </summary>
    public static IReadOnlyList<string> Broken(string html)
    {
        var broken = new List<string>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in Ids().Matches(html).Select(static match => match.Groups[1].Value))
        {
            if (!ids.Add(id))
            {
                broken.Add($"id \"{id}\" is declared twice");
            }
        }

        foreach (Match reference in References().Matches(html))
        {
            broken.AddRange(reference.Groups[2].Value
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(id => !ids.Contains(id))
                .Select(id => $"{reference.Groups[1].Value}=\"{reference.Groups[2].Value}\" names \"{id}\", which no element has"));
        }

        return broken;
    }

    /// <summary>How many attributes named <paramref name="attribute" /> hold a value that starts with <paramref name="prefix" />.</summary>
    public static int Count(string html, string attribute, string prefix) =>
        html.Split($" {attribute}=\"{prefix}", StringSplitOptions.None).Length - 1;

    /// <summary>
    ///     The page without what differs from one response to the next by design, and nothing else.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     The session id (<c>data-rask-root</c>, and wherever else the page repeats it) is a fresh random
    ///     token per response.
    ///     </para>
    ///     <para>
    ///     An element a component holds a reference to carries a token (<c>data-rask-ref</c>) made up for that
    ///     one instance, so the browser can find it: random, and a fresh one per response.
    ///     </para>
    ///     <para>
    ///     A generated id — <c>ui-tooltip-7</c>, <c>uisel-3-options</c>, <c>f-field-12</c> — carries a number drawn from one counter for
    ///     the whole process, so a later response draws higher numbers. Each is renumbered by the order it first
    ///     appears in: two pages then compare equal exactly when the same elements are joined the same way.
    ///     </para>
    /// </remarks>
    public static string Shape(string html)
    {
        var session = SessionId().Match(html).Groups[1].Value;
        var shaped = session.Length == 0 ? html : html.Replace(session, "SESSION", StringComparison.Ordinal);
        shaped = ElementRefs().Replace(shaped, "data-rask-ref=\"REF\"");
        var seen = new Dictionary<string, string>(StringComparer.Ordinal);

        return Generated().Replace(shaped, match =>
        {
            if (!seen.TryGetValue(match.Value, out var renumbered))
            {
                renumbered = match.Groups[1].Value + "-#" + seen.Count.ToString(CultureInfo.InvariantCulture);
                seen[match.Value] = renumbered;
            }

            return renumbered;
        });
    }

    /// <summary>
    ///     Where the pages stop being one document once each is <see cref="Shape" />d: the text around the first
    ///     difference in each, or nothing when they are all the same.
    /// </summary>
    public static string? Difference(IEnumerable<string> pages)
    {
        var shapes = pages.Select(Shape).Distinct(StringComparer.Ordinal).ToList();
        if (shapes.Count < 2)
        {
            return null;
        }

        var at = 0;
        while (at < shapes[0].Length && at < shapes[1].Length && shapes[0][at] == shapes[1][at])
        {
            at++;
        }

        return $"{shapes.Count} different documents; the first two part at {at}: …{Around(shapes[0], at)}… against …{Around(shapes[1], at)}…";
    }

    private static string Around(string text, int at) =>
        text[Math.Max(0, at - 120)..Math.Min(text.Length, at + 120)];

    [GeneratedRegex("\\sid=\"([^\"]*)\"", RegexOptions.None, 2000)]
    private static partial Regex Ids();

    [GeneratedRegex(
        "\\s(aria-labelledby|aria-describedby|aria-controls|aria-owns|aria-activedescendant|aria-errormessage|aria-details"
        + "|for|popovertarget|commandfor|list|form|data-rask-tooltip)=\"([^\"]*)\"",
        RegexOptions.None,
        2000)]
    private static partial Regex References();

    [GeneratedRegex("data-rask-ref=\"[0-9a-f]{32}\"", RegexOptions.None, 2000)]
    private static partial Regex ElementRefs();

    [GeneratedRegex("data-rask-root=\"([^\"]+)\"", RegexOptions.None, 2000)]
    private static partial Regex SessionId();

    [GeneratedRegex("\\b(ui[a-z]*(?:-[a-z]+)*|f-field)-\\d+", RegexOptions.None, 2000)]
    private static partial Regex Generated();
}
