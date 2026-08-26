namespace Rask.Spa.Hosting;

/// <summary>
///     Normalizes the prefix a bundle is served under.
/// </summary>
/// <remarks>
///     A copy of <c>Rask.Core.Live.RaskPath</c>, and a deliberate one: this package takes no
///     dependency on <c>Rask.Core</c>, which is what lets it serve a plain ASP.NET app. Ten lines of
///     duplication is the price of that, and the two must agree — a host running both this and
///     <c>Rask.Wasm.Hosting</c> under the same prefix would otherwise mount them one level apart.
/// </remarks>
internal static class SpaPath
{
    /// <summary>
    ///     Returns <c>""</c> for null, empty, whitespace or <c>"/"</c>; otherwise the value with a
    ///     leading slash ensured and every trailing slash stripped.
    /// </summary>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var s = value.Trim();
        if (s == "/")
        {
            return string.Empty;
        }

        if (s[0] != '/')
        {
            s = "/" + s;
        }

        while (s.Length > 1 && s[^1] == '/')
        {
            s = s[..^1];
        }

        return s;
    }
}
