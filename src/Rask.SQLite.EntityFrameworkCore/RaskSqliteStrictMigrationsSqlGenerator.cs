using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Rask.SQLite;

/// <summary>
/// A migrations SQL generator that emits <c>CREATE TABLE … ) STRICT</c>, so SQLite enforces each
/// column's declared type instead of coercing whatever it is handed. Registered by
/// <c>UseRaskSqlite(…, strictTables: true)</c>.
/// </summary>
/// <remarks>
/// <para>
/// SQLite is dynamically typed by default: a column's type is an <i>affinity</i>, a preference, and the
/// text <c>"lots"</c> stores happily in an <c>INTEGER</c> column. EF Core's model keeps C# honest, but
/// nothing stops a direct <c>INSERT</c>, an external tool or a legacy row from putting anything
/// anywhere — and a value of the wrong storage class then flows back into your app as a cast error, a
/// mis-ordered index, or worse. A <c>STRICT</c> table (SQLite 3.37+) rejects the write instead.
/// </para>
/// <para>
/// EF Core has no <c>STRICT</c> support of its own, and no hook for table options, so this overrides the
/// <c>CREATE TABLE</c> generation to suppress the terminator, append the keyword, and terminate. Table
/// rebuilds — which SQLite needs for most <c>ALTER</c>s, and which EF performs as create/copy/drop/rename —
/// go through the same operation, so the rebuilt table keeps its strictness.
/// </para>
/// <para>
/// Strictness is per table, so enabling this does not require a migration: existing tables stay as they
/// are and newly created ones are strict. Converting an existing table means rebuilding it.
/// </para>
/// </remarks>
public class RaskSqliteStrictMigrationsSqlGenerator : SqliteMigrationsSqlGenerator
{
    /// <summary>Creates the generator. Resolved by Entity Framework Core, not constructed directly.</summary>
    /// <param name="dependencies">The migrations SQL generator dependencies.</param>
    /// <param name="migrationsAnnotations">The relational annotation provider.</param>
    public RaskSqliteStrictMigrationsSqlGenerator(
        MigrationsSqlGeneratorDependencies dependencies,
        IRelationalAnnotationProvider migrationsAnnotations)
        : base(dependencies, migrationsAnnotations)
    {
    }

    /// <inheritdoc/>
    protected override void Generate(
        CreateTableOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder,
        bool terminate = true)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(builder);

        // Report a type STRICT cannot hold against the table and column it came from. SQLite's own
        // error names only the type, which is not much to go on when it surfaces during startup.
        foreach (var column in operation.Columns)
        {
            if (!SqliteStrictTypes.IsAllowed(column.ColumnType))
            {
                throw new InvalidOperationException(
                    SqliteStrictTypes.Describe(operation.Name, column.Name, column.ColumnType));
            }
        }

        // Suppress the base terminator so the keyword lands between the closing paren and the semicolon,
        // which is the only place SQLite's grammar accepts it.
        base.Generate(operation, model, builder, terminate: false);

        builder.Append(" STRICT");

        if (terminate)
        {
            builder.AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
            EndStatement(builder);
        }
    }
}
