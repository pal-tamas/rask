using System.Globalization;

namespace Rask.Core.Routing;

public static class RouteValueFormatter
{
    public static string Format(object? value) => value is null ? string.Empty : Uri.EscapeDataString(Text(value));

    // A value as text, before the URL encodes it: invariant culture, and a bool as true/false.
    internal static string Text(object value) => value switch
    {
        string s => s,
        bool b => b ? "true" : "false",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty
    };
}
