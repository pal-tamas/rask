namespace Rask.Printing;

/// <summary>How a PDF is laid out: <c>new PdfOptions { Page = PdfPage.A4.Landscape, Footer = "Visszatérítés 2026 Q3" }</c>.</summary>
public sealed class PdfOptions
{
    /// <summary>The paper; A4 upright unless set.</summary>
    public PdfPage Page { get; init; } = PdfPage.A4;

    /// <summary>The white border around every page; 10 mm unless set.</summary>
    public PdfMargins Margins { get; init; } = PdfMargins.Millimetres(10);

    /// <summary>A line printed at the foot of every page, left; unset, the foot carries only the page numbers.</summary>
    public string? Footer { get; init; }

    /// <summary>Prints "1 / 5" at the foot of every page, right. On unless switched off.</summary>
    public bool PageNumbers { get; init; } = true;

    /// <summary>
    /// Writes a tagged PDF — the document's headings, tables and reading order, for screen readers. Off unless
    /// asked for: the structure is most of the file, a 209-page table comes out 12 MB tagged and 0.45 MB untagged.
    /// </summary>
    public bool Tagged { get; init; }

    internal bool HasFoot => Footer is not null || PageNumbers;
}
