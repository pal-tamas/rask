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
}
