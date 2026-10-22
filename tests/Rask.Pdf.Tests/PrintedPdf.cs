using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace Rask.Printing.Tests;

/// <summary>Reads a printed PDF back: its pages' sizes and the words on them.</summary>
internal static class PrintedPdf
{
    public static string Html(string body) =>
        "<!doctype html><html lang=\"hu\"><head><meta charset=\"utf-8\">"
        + "<style>body { font-family: sans-serif; font-size: 12pt; } td, th { padding: 4px 8px; }</style>"
        + "</head><body>" + body + "</body></html>";

    public static IReadOnlyList<(double Width, double Height, string Text)> Pages(byte[] pdf)
    {
        using var document = PdfDocument.Open(pdf);
        return document.GetPages().Select(page => (page.Width, page.Height, Words(page))).ToList();
    }

    /// <summary>How far from the left edge, in points, the first page's first letter starts.</summary>
    public static double FirstLetterLeft(byte[] pdf)
    {
        using var document = PdfDocument.Open(pdf);
        return document.GetPage(1).Letters[0].StartBaseLine.X;
    }

    /// <summary>The fonts the first page's letters are drawn in.</summary>
    public static IReadOnlySet<string> Fonts(byte[] pdf)
    {
        using var document = PdfDocument.Open(pdf);
        return document.GetPage(1).Letters.Select(letter => letter.FontName ?? string.Empty).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>How many images the PDF holds — a CSS background is drawn as a pattern, so counted in the file, not on a page.</summary>
    public static int Images(byte[] pdf) => Regex.Count(Encoding.Latin1.GetString(pdf), "/Subtype\\s*/Image");

    private static string Words(Page page) => string.Join(' ', page.GetWords().Select(word => word.Text));
}
