namespace Rask.Workbooks;

/// <summary>
///     How much one read may take: a part of the package unpacked (a zip bomb unpacks without end) and the cells of a
///     sheet held in memory. Both are far above any workbook Excel itself opens comfortably.
/// </summary>
internal sealed record ReadLimits(long MaxPartBytes, int MaxCells)
{
    public static readonly ReadLimits Default = new(256L * 1024 * 1024, 10_000_000);
}
