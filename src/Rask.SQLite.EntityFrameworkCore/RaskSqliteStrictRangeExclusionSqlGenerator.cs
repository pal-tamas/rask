using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Rask.Data;

namespace Rask.SQLite;

/// <summary>
/// Both of the things Rask adds to SQLite's migration SQL at once: <c>STRICT</c> tables and the DDL enforcing
/// each <see cref="RangeExclusionSpec"/>. Registered by <c>UseRaskSqlite(..., strictTables: true)</c>.
/// </summary>
/// <remarks>
/// EF Core resolves exactly one <see cref="IMigrationsSqlGenerator"/>, so registering
/// <see cref="RaskSqliteStrictMigrationsSqlGenerator"/> and
/// <see cref="RaskSqliteRangeExclusionSqlGenerator"/> both would silently keep only whichever was replaced
/// last — the app would build, migrate and pass its tests with one of the two features quietly missing. This
/// type exists so that combination is expressible: it inherits the strict <c>CREATE TABLE</c> and composes
/// the shared range DDL, which is why that DDL lives in <see cref="RangeExclusionDdl"/> rather than in a
/// generator.
/// </remarks>
/// <param name="dependencies">Generator dependencies, supplied by EF Core.</param>
/// <param name="migrationsAnnotations">Provider annotations, supplied by EF Core.</param>
public sealed class RaskSqliteStrictRangeExclusionSqlGenerator(
    MigrationsSqlGeneratorDependencies dependencies,
    IRelationalAnnotationProvider migrationsAnnotations)
    : RaskSqliteStrictMigrationsSqlGenerator(dependencies, migrationsAnnotations)
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
