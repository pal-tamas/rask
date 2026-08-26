using System.Globalization;

namespace Rask.Core.Globalization;

/// <summary>
///     Reads and writes ASP.NET's culture-cookie payload, <c>c=hu-HU|uic=hu-HU</c>.
/// </summary>
/// <remarks>
///     Hand-written rather than taken from <c>Microsoft.AspNetCore.Localization</c>: this lives in
///     <c>Rask.Core</c>, which has no ASP.NET dependency and also runs in the browser. The format is
///     reproduced exactly so that a Rask app sitting beside MVC — or one that also calls
///     <c>UseRequestLocalization()</c> — reads and writes the same preference rather than a second,
///     disagreeing one. See <see cref="RaskCultureOptions.CookieName" />.
/// </remarks>
public static class RaskCultureCookie
{
    private const string CulturePrefix = "c=";
    private const string UICulturePrefix = "uic=";

    /// <summary>Formats a cookie value for the given cultures.</summary>
    public static string Format(string culture, string? uiCulture = null) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{CulturePrefix}{culture}|{UICulturePrefix}{uiCulture ?? culture}");

    /// <summary>
    ///     Parses a cookie value. Tolerates a bare tag (<c>"hu-HU"</c>) as well as the paired form, and
    ///     answers <c>false</c> for anything it cannot read rather than throwing — a malformed cookie is
    ///     a visitor with a stale or hand-edited browser, not a server error.
    /// </summary>
    public static bool TryParse(string? value, out string culture, out string uiCulture)
    {
        culture = string.Empty;
        uiCulture = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        foreach (var range in value.AsSpan().Split('|'))
        {
            var part = value.AsSpan()[range].Trim();
            if (part.StartsWith(CulturePrefix, StringComparison.Ordinal))
            {
                culture = part[CulturePrefix.Length..].ToString();
            }
            else if (part.StartsWith(UICulturePrefix, StringComparison.Ordinal))
            {
                uiCulture = part[UICulturePrefix.Length..].ToString();
            }
            else if (culture.Length == 0 && !part.IsEmpty && !part.Contains('='))
            {
                // A bare tag, which is what a hand-set cookie usually contains. Whether it names a real
                // culture is RaskCultureResolver's question, not this parser's.
                culture = part.ToString();
            }
        }

        if (culture.Length == 0)
        {
            culture = uiCulture;
        }

        if (uiCulture.Length == 0)
        {
            uiCulture = culture;
        }

        return culture.Length > 0;
    }
}
