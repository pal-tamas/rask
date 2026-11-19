namespace Rask.Workbooks;

/// <summary>
///     How much one read may take: a part of the package unpacked (a zip bomb unpacks without end) and the cells of a
///     sheet held in memory. The cells are what a read costs in memory — each one a slot and its value, so ten million
///     were gigabytes for one uploaded file, and in a browser tab more than it has. Two million is 100,000 rows of twenty
///     columns, above what an import is asked to take.
/// </summary>
internal sealed record ReadLimits(long MaxPartBytes, int MaxCells)
{
    public static readonly ReadLimits Default = new(256L * 1024 * 1024, 2_000_000);
}
