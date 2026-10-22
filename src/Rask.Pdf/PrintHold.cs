using System.Text;

namespace Rask.Printing;

/// <summary>
/// Keeps a printed document out of rendering until it has loaded. Chromium draws a page while it is still parsing
/// it, and every frame restyles and lays out the table grown since the last: a long table costs the square of its
/// rows — 10,000 kit table rows took 15 seconds to load on a laptop. Held, the document is parsed without being
/// drawn, then styled and laid out once.
/// </summary>
internal static class PrintHold
{
    /// <summary>The id of the stylesheet that holds the document.</summary>
    internal const string Id = "rask-print-hold";

    private const char ByteOrderMark = (char)0xFEFF;

    // In a layer of its own, the first the document names: an important declaration of an earlier layer outranks every
    // later one and every unlayered one, so no `display: block !important` of the page's own lets the drawing start.
    private const string Sheet = "<style id=\"" + Id + "\">@layer " + Id + " { :root { display: none !important; } }</style>";

    /// <summary>
    /// Removes the hold, lays the document out (so a web font or image the layout asks for is asked for now), and
    /// waits for its web fonts. Evaluated by the protocol: the page's own scripts stay off.
    /// </summary>
    internal const string Release =
        "document.getElementById('" + Id + "')?.remove(); document.documentElement.getBoundingClientRect(); "
        + "document.fonts.ready.then(() => true)";

    /// <summary>
    /// <paramref name="html"/> as UTF-8, with the hold as its first element: after the doctype, which must stay first
    /// or the page is laid out in quirks mode. Encoded in place, so a long document is not copied once more to insert it.
    /// </summary>
    public static byte[] Utf8(string html)
    {
        ArgumentNullException.ThrowIfNull(html);
        var at = AfterPrologue(html);
        var prologue = html.AsSpan(0, at);
        var rest = html.AsSpan(at);
        var bytes = new byte[Encoding.UTF8.GetByteCount(prologue) + Encoding.UTF8.GetByteCount(Sheet) + Encoding.UTF8.GetByteCount(rest)];
        var written = Encoding.UTF8.GetBytes(prologue, bytes);
        written += Encoding.UTF8.GetBytes(Sheet, bytes.AsSpan(written));
        Encoding.UTF8.GetBytes(rest, bytes.AsSpan(written));
        return bytes;
    }

    // Past the whitespace, comments and doctype that may open a document.
    private static int AfterPrologue(string html)
    {
        var at = 0;
        while (true)
        {
            while (at < html.Length && (char.IsWhiteSpace(html[at]) || html[at] == ByteOrderMark))
            {
                at++;
            }

            if (string.CompareOrdinal(html, at, "<!--", 0, 4) == 0)
            {
                var end = html.IndexOf("-->", at + 4, StringComparison.Ordinal);
                if (end < 0)
                {
                    return at;
                }

                at = end + 3;
            }
            else if (string.Compare(html, at, "<!doctype", 0, 9, StringComparison.OrdinalIgnoreCase) == 0)
            {
                var end = html.IndexOf('>', at);
                return end < 0 ? at : end + 1;
            }
            else
            {
                return at;
            }
        }
    }
}
