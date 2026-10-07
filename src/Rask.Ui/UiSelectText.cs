using System.Globalization;
using System.Text;
using Rask.Core;
using Rask.Core.Components;

namespace Rask;

/// <summary>The words inside an option's children: what a list reads, searches and types ahead by.</summary>
internal static class UiSelectText
{
    /// <summary>Every piece of text under <paramref name="children" />, in order, single-spaced.</summary>
    internal static string Of(IEnumerable<Component?>? children)
    {
        if (children is null)
        {
            return string.Empty;
        }

        var text = new StringBuilder();
        Collect(children, text);

        return text.ToString().Trim();
    }

    /// <summary>
    ///     Flux's own matching: whether <paramref name="search" /> is anywhere in <paramref name="words" />,
    ///     whatever the case or the accents.
    /// </summary>
    internal static bool Contains(string words, string search) =>
        CultureInfo.CurrentCulture.CompareInfo.IndexOf(
            words, search, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0;

    /// <summary>Whether <paramref name="children" /> are text and nothing else.</summary>
    internal static bool IsTextOnly(IEnumerable<Component?> children) =>
        children.All(child => child is null or Text);

    // Elements are walked; a component of the call site's own is not run to find out what it would say.
    private static void Collect(IEnumerable<Component?> children, StringBuilder text)
    {
        foreach (var child in children)
        {
            if (child is Text { Value: { } value })
            {
                text.Append(text.Length > 0 && !char.IsWhiteSpace(text[^1]) && !StartsWithSpace(value) ? " " : string.Empty)
                    .Append(value);
            }
            else if (child is Element { Children: { } nested })
            {
                Collect(nested, text);
            }
        }
    }

    private static bool StartsWithSpace(string value) => value.Length > 0 && char.IsWhiteSpace(value[0]);
}
