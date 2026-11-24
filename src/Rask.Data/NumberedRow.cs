namespace Rask.Data;

/// <summary>What a numbering reads of one row: its key, its number, whether it is counted, and where it is counted.</summary>
internal sealed class NumberedRow
{
    public object? Key { get; set; }

    public object? Number { get; set; }

    public bool Skipped { get; set; }

    public object? Within { get; set; }

    public object? First { get; set; }

    public object? Same { get; set; }
}
