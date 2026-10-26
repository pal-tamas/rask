namespace Rask.Data;

/// <summary>A constraint or index being named by <c>ConstraintNames</c>: what it is, and where.</summary>
public sealed class Constraint
{
    internal Constraint(ConstraintKind kind, string table, IReadOnlyList<string> columns)
    {
        Kind = kind;
        Table = table;
        Columns = columns;
    }

    /// <summary>What the constraint is.</summary>
    public ConstraintKind Kind { get; }

    /// <summary>The table it is declared on — for a foreign key, the table holding the key.</summary>
    public string Table { get; }

    /// <summary>Its columns, in order, as the database names them.</summary>
    public IReadOnlyList<string> Columns { get; }

    /// <summary>The columns joined by <c>_</c>: <c>TenantId_Email</c>.</summary>
    public string Joined => string.Join('_', Columns);
}
