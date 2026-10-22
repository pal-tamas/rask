using System.Net;
using Rask.Core.Globalization;

namespace Rask.Printing;

/// <summary>
/// The page numbers at the foot of a page, in the caller's language: <c>RaskString.PdfPageNumber</c>, which an app
/// translates like any catalog text, with Chromium's own page and page-count spans where its <c>{0}</c> and
/// <c>{1}</c> are.
/// </summary>
internal static class PageNumberFoot
{
    /// <summary>The English, and the text a translation that does not fit falls back to.</summary>
    public const string English = "{0} / {1}";

    /// <summary>The English as the foot writes it, for a document printed outside any work in progress.</summary>
    public const string EnglishHtml = """<span class="pageNumber"></span> / <span class="totalPages"></span>""";

    // Stand-ins for the two spans while the text is escaped: private-use characters, which HTML encoding leaves as
    // they are and no translation writes.
    internal const string Page = "\uE000";
    internal const string Pages = "\uE001";

    /// <summary>The foot's page numbers as HTML, read from the catalog in the language of the work in progress.</summary>
    public static string Html() => Html(RaskStrings.Get(RaskString.PdfPageNumber, English, Page, Pages));

    /// <summary><paramref name="text"/>, escaped, with its stand-ins turned into the spans Chromium fills in.</summary>
    internal static string Html(string text) => WebUtility.HtmlEncode(text)
        .Replace(Page, """<span class="pageNumber"></span>""", StringComparison.Ordinal)
        .Replace(Pages, """<span class="totalPages"></span>""", StringComparison.Ordinal);
}
