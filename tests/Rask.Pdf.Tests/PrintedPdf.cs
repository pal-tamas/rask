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

    private static string Words(Page page) => string.Join(' ', page.GetWords().Select(word => word.Text));
}
