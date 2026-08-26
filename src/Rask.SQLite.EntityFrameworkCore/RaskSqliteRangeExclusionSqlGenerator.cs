// EF1001: SqliteMigrationsSqlGenerator is EF-internal, but subclassing the provider's generator is the only
// seam that can add DDL to a migration while keeping every SQLite behaviour the provider implements (notably
// its table-rebuild rewriting). The base type moves with the EF Core version this package already pins, so an
// EF Core major upgrade must re-verify this file — the RangeExclusion tests cover exactly that.
#pragma warning disable EF1001

using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Sqlite.Migrations.Internal;
using Rask.Data;

namespace Rask.SQLite;

/// <summary>
/// Emits the DDL enforcing each <see cref="RangeExclusionSpec"/> the model declares, on top of the SQLite
/// provider's own migration SQL. Registered by <c>UseRaskSqlite(...)</c>.
/// </summary>
/// <remarks>
/// The DDL is appended at the <em>end</em> of the migration rather than attached to the <c>CREATE TABLE</c>,
/// and always drops before it creates. That matters because SQLite cannot <c>ALTER</c> most things in place:
/// EF's provider rebuilds the table (create <c>ef_temp_*</c>, copy, <c>DROP TABLE</c>, rename), which takes
/// the old triggers with it. Re-emitting last restores them whichever path the provider took, and the
/// drop-then-create keeps it idempotent.
/// </remarks>
/// <param name="dependencies">Generator dependencies, supplied by EF Core.</param>
/// <param name="migrationsAnnotations">Provider annotations, supplied by EF Core.</param>
public class RaskSqliteRangeExclusionSqlGenerator(
    MigrationsSqlGeneratorDependencies dependencies,
    IRelationalAnnotationProvider migrationsAnnotations)
    : SqliteMigrationsSqlGenerator(dependencies, migrationsAnnotations)
{
    /// <inheritdoc />
    public override IReadOnlyList<MigrationCommand> Generate(
        IReadOnlyList<MigrationOperation> operations,
        IModel? model,
        MigrationsSqlGenerationOptions options = MigrationsSqlGenerationOptions.Default)
    {
        var commands = base.Generate(operations, model, options).ToList();
        commands.AddRange(RangeExclusionDdl.Build(operations, model, Dependencies));
        return commands;
    }
}
