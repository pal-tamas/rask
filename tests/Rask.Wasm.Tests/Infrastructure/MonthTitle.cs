using System.Globalization;
using System.Net;
using Rask.Core;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated chain entries

namespace Rask.Wasm.Tests.Infrastructure;

// What the kit's calendar titles a month with: the culture's own year-month pattern.
internal sealed partial class MonthTitle : Component
{
    /// <summary>The title as it reads in a rendered document.</summary>
    public static string In(string html)
    {
        var start = html.IndexOf("<h2>", StringComparison.Ordinal) + "<h2>".Length;
        return WebUtility.HtmlDecode(html[start..html.IndexOf("</h2>", start, StringComparison.Ordinal)]);
    }

    protected override Component? Render() =>
        H2[new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Unspecified).ToString("Y", CultureInfo.CurrentCulture)];
}
