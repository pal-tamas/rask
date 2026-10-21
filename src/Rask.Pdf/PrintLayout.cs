using System.Globalization;
using System.Net;
using System.Text.Json;

namespace Rask.Printing;

/// <summary>Turns <see cref="PdfOptions"/> into the parameters of Chromium's <c>Page.printToPDF</c>.</summary>
internal static class PrintLayout
{
    // The foot is drawn inside the bottom margin, so a margin too thin for a line of 8pt text is widened to fit it.
    private const double FootMillimetres = 12;

    /// <summary>Writes paper, margins, foot, tagging and the page range.</summary>
    /// <param name="json">The command's parameters being written.</param>
    /// <param name="options">The layout asked for.</param>
    /// <param name="lastPage">The last page printed: one past the limit, so a longer document is caught, not cut.</param>
    public static void Write(Utf8JsonWriter json, PdfOptions options, int lastPage)
    {
        var page = options.Page;
        var margins = options.Margins;
        json.WriteBoolean("landscape", page.IsLandscape);
        json.WriteBoolean("printBackground", true);
        json.WriteBoolean("generateTaggedPDF", options.Tagged);
        json.WriteNumber("paperWidth", page.WidthInches);
        json.WriteNumber("paperHeight", page.HeightInches);
        json.WriteNumber("marginTop", PdfMargins.Inches(margins.Top));
        json.WriteNumber("marginRight", PdfMargins.Inches(margins.Right));
        json.WriteNumber("marginBottom", PdfMargins.Inches(options.HasFoot ? Math.Max(margins.Bottom, FootMillimetres) : margins.Bottom));
        json.WriteNumber("marginLeft", PdfMargins.Inches(margins.Left));
        json.WriteString("pageRanges", string.Create(CultureInfo.InvariantCulture, $"1-{lastPage}"));
        json.WriteBoolean("displayHeaderFooter", options.HasFoot);
        if (options.HasFoot)
        {
            json.WriteString("headerTemplate", "<span></span>");
            json.WriteString("footerTemplate", Foot(options));
        }
    }

    // Chromium fills the pageNumber and totalPages spans itself. The template has no stylesheet of the page's and a
    // zero default font size, so it carries its own.
    private static string Foot(PdfOptions options) => string.Create(
        CultureInfo.InvariantCulture,
        $"""<div style="box-sizing:border-box;width:100%;padding:0 {options.Margins.Right}mm 0 {options.Margins.Left}mm;display:flex;justify-content:space-between;font:8pt sans-serif;color:#52525b"><span>{WebUtility.HtmlEncode(options.Footer ?? string.Empty)}</span><span>{(options.PageNumbers ? """<span class="pageNumber"></span> / <span class="totalPages"></span>""" : string.Empty)}</span></div>""");
}
