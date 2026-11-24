namespace Rask.Data;

/// <summary>
///     A write a declared lock refuses, for a message to say it in its own words:
///     <c>(r, w) =&gt; w.Kind == LockedWriteKind.Delete ? $"{r.Name} is filed: it keeps its transactions." : …</c>.
/// </summary>
/// <typeparam name="T">The row that is locked.</typeparam>
public sealed class LockedWrite<T>
{
    internal LockedWrite(LockedWriteKind kind, T row) => (Kind, Row) = (kind, row);

    /// <summary>What the write does to the row.</summary>
    public LockedWriteKind Kind { get; }

    /// <summary>The row, as the save would write it.</summary>
    public T Row { get; }
}
