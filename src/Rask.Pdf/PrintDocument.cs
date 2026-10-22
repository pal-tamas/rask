namespace Rask.Printing;

/// <summary>One document to print: its HTML, the path it answers at (so relative links resolve), and its layout.</summary>
/// <param name="Html">The whole document.</param>
/// <param name="Path">The path it is served under, <c>/reports/7</c>; its stylesheet and fonts are found from there.</param>
/// <param name="Options">Paper, margins, foot and tagging.</param>
/// <param name="Assets">The app's static files; null serves nothing but the document.</param>
internal sealed record PrintDocument(string Html, string Path, PdfOptions Options, IPrintAssets? Assets = null)
{
    /// <summary>The foot's page numbers as HTML, in the caller's language; the English unless set.</summary>
    public string PageNumbers { get; init; } = PageNumberFoot.EnglishHtml;

    /// <summary>A bare HTML document with nothing to load beside it.</summary>
    public static PrintDocument Of(string html, PdfOptions? options = null) => new(html, "/", options ?? new PdfOptions());
}
