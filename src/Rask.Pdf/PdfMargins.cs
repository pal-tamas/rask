namespace Rask.Printing;

/// <summary>The white border around a printed page: <c>PdfMargins.Millimetres(15)</c>, or top/sides apart.</summary>
public sealed class PdfMargins
{
    private const double MillimetresPerInch = 25.4;

    private PdfMargins(double top, double right, double bottom, double left)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(top);
        ArgumentOutOfRangeException.ThrowIfNegative(right);
        ArgumentOutOfRangeException.ThrowIfNegative(bottom);
        ArgumentOutOfRangeException.ThrowIfNegative(left);
        Top = top;
        Right = right;
        Bottom = bottom;
        Left = left;
    }

    /// <summary>The same border on every side.</summary>
    public static PdfMargins Millimetres(double all) => new(all, all, all, all);

    /// <summary>One border above and below, another left and right.</summary>
    public static PdfMargins Millimetres(double vertical, double horizontal) => new(vertical, horizontal, vertical, horizontal);

    internal double Top { get; }

    internal double Right { get; }

    internal double Bottom { get; }

    internal double Left { get; }

    internal static double Inches(double millimetres) => millimetres / MillimetresPerInch;
}
