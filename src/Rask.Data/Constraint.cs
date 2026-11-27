using Rask.Generators.Shared;

namespace Rask.Data;

/// <summary>A constraint or index being named by <c>ConstraintNames</c>: what it is, and where.</summary>
public sealed class Constraint
{
    internal Constraint(ConstraintKind kind, string table, IReadOnlyList<string> columns, string? asDeclared)
    {
        Kind = kind;
        Table = table;
        Columns = columns;
        AsDeclared = asDeclared;
    }

    /// <summary>What the constraint is.</summary>
    public ConstraintKind Kind { get; }

    /// <summary>The table it is declared on — for a foreign key, the table holding the key.</summary>
    public string Table { get; }

    /// <summary>Its columns, in order, as the database names them. A check constraint has none.</summary>
    public IReadOnlyList<string> Columns { get; }

    /// <summary>The columns joined by <c>_</c>: <c>tenant_id_email</c>.</summary>
    public string Joined => string.Join('_', Columns);

    /// <summary>
    ///     What EF Core names it — <c>IX_fuelings_pistol_id</c> — or, for a check constraint, the name the model gave it
    ///     (<c>EndsAfterStart</c>).
    /// </summary>
    public string? AsDeclared { get; }

    /// <summary>
    ///     Rask's own name for it — its kind's prefix, the table and the columns — what a model names it with no
    ///     <c>ConstraintNames</c>: <c>pk_fuelings</c>, <c>fk_fuelings_pistol_id</c>, <c>uq_users_tenant_key_email</c>,
    ///     <c>ix_fuelings_pistol_id</c>, <c>ck_bookings_ends_after_start</c>, <c>df_jobs_awaited</c>.
    /// </summary>
    /// <remarks>Cut to 63 bytes, PostgreSQL's limit, on every database: a longer one ends in a hash of the whole name.</remarks>
    public string Prefixed => Kind switch
    {
        ConstraintKind.PrimaryKey => StoreNames.Constraint(StoreNames.PrimaryKey, Table, []),
        ConstraintKind.ForeignKey => StoreNames.Constraint(StoreNames.ForeignKey, Table, Columns),
        ConstraintKind.Unique => StoreNames.Constraint(StoreNames.Unique, Table, Columns),
        ConstraintKind.Index => StoreNames.Constraint(StoreNames.Index, Table, Columns),
        ConstraintKind.Check => StoreNames.Constraint(StoreNames.Check, Table, [StoreNames.SnakeCase(AsDeclared ?? "")]),
        _ => StoreNames.Constraint(StoreNames.Default, Table, Columns),
    };
}
