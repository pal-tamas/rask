using System.Text;

namespace Rask.Printing;

/// <summary>Counts the pages of a PDF Chromium printed, without a PDF library.</summary>
/// <remarks>
/// Chromium (Skia) writes every object as plain text — no object streams — and each page as one dictionary with
/// <c>/Type /Page</c>; the page tree's own dictionaries say <c>/Type /Pages</c>, which the next byte tells apart.
/// </remarks>
internal static class PdfPageCount
{
    private static readonly byte[] PageType = Encoding.ASCII.GetBytes("/Type /Page");

    public static int Of(ReadOnlySpan<byte> pdf)
    {
        var count = 0;
        for (var at = pdf.IndexOf(PageType); at >= 0; at = Next(pdf, at))
        {
            var after = at + PageType.Length;
            if (after >= pdf.Length || pdf[after] != (byte)'s')
            {
                count++;
            }
        }

        return count;
    }

    private static int Next(ReadOnlySpan<byte> pdf, int at)
    {
        var from = at + PageType.Length;
        var found = pdf[from..].IndexOf(PageType);
        return found < 0 ? -1 : from + found;
    }
}
