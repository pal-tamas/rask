namespace Rask.Data;

/// <summary>What a <see cref="Constraint" /> being named is.</summary>
public enum ConstraintKind
{
    /// <summary>A table's primary key.</summary>
    PrimaryKey,

    /// <summary>A foreign key to another table.</summary>
    ForeignKey,

    /// <summary>A unique index or an alternate key.</summary>
    Unique,

    /// <summary>An index that is not unique.</summary>
    Index,

    /// <summary>A column's default value — a named constraint on SQL Server only.</summary>
    Default,
}
