namespace Rask.Data;

/// <summary>One column on a read face, and where it comes from on the write model.</summary>
/// <param name="member">The property on the read face — <c>TotalAmount</c>.</param>
/// <param name="path">
///     The dotted path to it on the write model — <c>Total.Amount</c> — used to find what EF actually decided.
/// </param>
/// <param name="columnName">
///     The column the convention says it lands on. The mirror overrides this wherever the write model
///     disagrees, so it only decides for a property the write model has nothing to say about.
/// </param>
public sealed class ReadColumnMapping(string member, string path, string columnName)
{
    /// <summary>The property on the read face.</summary>
    public string Member { get; } = member;

    /// <summary>The dotted path to the same value on the write model.</summary>
    public string Path { get; } = path;

    /// <summary>The column name the convention derives.</summary>
    public string ColumnName { get; } = columnName;
}
