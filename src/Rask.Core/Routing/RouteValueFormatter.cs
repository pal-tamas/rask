using System.Collections;
using System.Globalization;
using System.Text;

namespace Rask.Core.Routing;

public static class RouteValueFormatter
{
    /// <summary>
    ///     A route or query value as its URL writes it, encoded. A list — a <c>[QueryParam]</c> one — is its
    ///     elements joined by commas, <c>ada%2Ctheo</c>; an element that is null or writes as nothing is left out.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns>The encoded value; empty for null, or for a list with nothing to write.</returns>
    public static string Format(object? value) =>
        value switch
        {
            null => string.Empty,
            IEnumerable list and not string and not IFormattable => Uri.EscapeDataString(Joined(list)),
            _ => Uri.EscapeDataString(Text(value))
        };

    // A value as text before the URL encodes it.
    private static string Text(object value) => value switch
    {
        string s => s,
        bool b => b ? "true" : "false",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty
    };

    private static string Joined(IEnumerable list)
    {
        var joined = new StringBuilder();
        foreach (var element in list)
        {
            if (element is null || Text(element) is not { Length: > 0 } text)
            {
                continue;
            }

            if (joined.Length > 0)
            {
                joined.Append(',');
            }

            joined.Append(text);
        }

        return joined.ToString();
    }
}
