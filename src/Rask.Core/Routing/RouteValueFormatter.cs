using System.Globalization;

namespace Rask.Core.Routing;

public static class RouteValueFormatter
{
    public static string Format(object? value)
    {
        return value switch
        {
            null => string.Empty,
            string s => Uri.EscapeDataString(s),
            bool b => b ? "true" : "false",
            IFormattable f => Uri.EscapeDataString(f.ToString(null, CultureInfo.InvariantCulture)),
            _ => Uri.EscapeDataString(value.ToString() ?? string.Empty)
        };
    }

    /// <summary>
    ///     A <c>[QueryParam]</c> list as its URL writes it: the elements in order, joined by commas —
    ///     <c>ada,theo</c> — or nothing when no element has anything to write, which leaves the key out.
    /// </summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="values">The elements; a null one, or one that writes as nothing, is left out.</param>
    /// <returns>The value, URL-encoded except for the commas between elements.</returns>
    public static string FormatList<T>(IEnumerable<T>? values) =>
        QueryList.Value(values) is { } value ? QueryString.EscapeValue(value) : string.Empty;

    // A value as text before the URL encodes it, the same way Format writes it.
    internal static string Text(object value) => value switch
    {
        string s => s,
        bool b => b ? "true" : "false",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty
    };
}
