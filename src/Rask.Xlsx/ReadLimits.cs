namespace Rask.Workbooks;

/// <summary>
///     How much one read may take: a part of the package unpacked (a zip bomb unpacks without end) and the cells of a
///     sheet held in memory. The cells are what a read costs in memory — each one a slot and its value, so ten million
///     were gigabytes for one uploaded file, and in a browser tab more than it has. The default two million is 100,000 rows
///     of twenty columns; a loader that is handed bigger sheets says so on its own read:
///     <c>Xlsx.Read(file, new ReadLimits { MaxCells = 5_000_000 })</c>.
/// </summary>
public sealed class ReadLimits
{
    /// <summary>The limits every read without its own takes.</summary>
    public ReadLimits()
    {
    }

    internal ReadLimits(long maxPartBytes, int maxCells)
    {
        MaxPartBytes = maxPartBytes;
        MaxCells = maxCells;
    }

    /// <summary>The limits a read takes when it names none.</summary>
    public static ReadLimits Default { get; } = new();

    /// <summary>The most one part of the file may unpack to. Default 256 MB.</summary>
    public long MaxPartBytes { get; init; } = 256L * 1024 * 1024;

    /// <summary>The most cells a sheet may hold. Default 2,000,000.</summary>
    public int MaxCells { get; init; } = 2_000_000;
}
