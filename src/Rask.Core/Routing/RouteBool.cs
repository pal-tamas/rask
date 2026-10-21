namespace Rask.Core.Routing;

/// <summary>
///     How a route segment or a query value reads as a <see cref="bool" />: <c>true</c>/<c>false</c> in any case, and
///     <c>1</c>/<c>0</c>, as Laravel's <c>boolean</c> rule reads them. Nothing else — not <c>on</c>, not <c>yes</c>.
/// </summary>
internal static class RouteBool
{
    public static bool TryParse(string text, out bool value)
    {
        switch (text)
        {
            case "1":
                value = true;
                return true;
            case "0":
                value = false;
                return true;
            default:
                return bool.TryParse(text, out value);
        }
    }
}
