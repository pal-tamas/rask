namespace Rask.Printing;

/// <summary>The paper a PDF is printed on: <c>PdfPage.A4</c>, or <c>PdfPage.A4.Landscape</c>.</summary>
public sealed class PdfPage
{
    private const double MillimetresPerInch = 25.4;

    private PdfPage(double widthMillimetres, double heightMillimetres, bool landscape)
    {
        WidthInches = widthMillimetres / MillimetresPerInch;
        HeightInches = heightMillimetres / MillimetresPerInch;
        IsLandscape = landscape;
    }

    /// <summary>ISO A4, 210 × 297 mm, upright.</summary>
    public static PdfPage A4 { get; } = new(210, 297, landscape: false);

    /// <summary>The same paper turned on its side.</summary>
    public PdfPage Landscape => IsLandscape ? this : new PdfPage(
        WidthInches * MillimetresPerInch, HeightInches * MillimetresPerInch, landscape: true);

    /// <summary>The paper's width as it comes out of the printer, before it is turned.</summary>
    internal double WidthInches { get; }

    /// <summary>The paper's height as it comes out of the printer, before it is turned.</summary>
    internal double HeightInches { get; }

    internal bool IsLandscape { get; }
}
