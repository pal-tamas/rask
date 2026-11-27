using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Rask.Data.Tests.NamingShapes;

namespace Rask.Providers.E2E.Tests;

/// <summary>
///     The migration an existing app gets once Rask names its model: from EF Core's names and a few stated by hand to
///     Rask's own, nothing is dropped and nothing added — every table, column, index and constraint is renamed.
/// </summary>
/// <remarks>Proved without a server: the differ compares two models, and a model needs a provider but no connection.</remarks>
public sealed class NamingRenameDiffTests
{
    private const string OfflineSqlServer = "Server=(local);Database=rask_model_only;Trusted_Connection=True";
    private const string OfflinePostgres = "Host=localhost;Database=rask_model_only";

    [Fact]
    public void On_sqlite_the_next_migration_only_renames()
    {
        using var legacy = new LegacyNamedContext(new DbContextOptionsBuilder<LegacyNamedContext>().UseRaskSqliteAt("Data Source=:memory:").Options);
        using var named = new RaskNamedContext(new DbContextOptionsBuilder<RaskNamedContext>().UseRaskSqliteAt("Data Source=:memory:").Options);

        var operations = Differences(legacy, named);

        Assert.All(operations, o => Assert.Contains(o.GetType(), SqliteRenames));
        Assert.Contains(operations, o => o is RenameTableOperation { Name: "Fueling", NewName: "fuelings" });
        Assert.Contains(operations, o => o is RenameColumnOperation { Name: "pistolId", NewName: "pistol_id" });
    }

    [Fact]
    public void On_sql_server_the_next_migration_only_renames_and_renames_each_constraint_in_place()
    {
        using var legacy = new LegacyNamedContext(new DbContextOptionsBuilder<LegacyNamedContext>().UseRaskSqlServerAt(OfflineSqlServer).Options);
        using var named = new RaskNamedContext(new DbContextOptionsBuilder<RaskNamedContext>().UseRaskSqlServerAt(OfflineSqlServer).Options);

        var operations = Differences(legacy, named);
        var sql = operations.OfType<SqlOperation>().Select(static s => s.Sql).ToList();

        Assert.All(operations, o => Assert.Contains(o.GetType(), Renames));
        Assert.Contains("EXEC sp_rename N'[Fueling_pkey]', N'pk_fuelings', N'OBJECT';", sql);
        Assert.Contains(sql, s => Renamed(s, "sp_rename N'[FK_Fueling_Pistol", "N'fk_fuelings_pistol_id', N'OBJECT'"));
        Assert.Contains(sql, s => Renamed(s, "sp_rename N'[AK_Pistol_Number]'", "N'uq_pistols_number', N'OBJECT'"));
        Assert.Contains(sql, s => s.Contains("c.name = N'flagged'", StringComparison.Ordinal) && s.Contains("N'df_fuelings_flagged', N'OBJECT'", StringComparison.Ordinal));
    }

    [Fact]
    public void On_postgresql_the_next_migration_only_renames_and_renames_each_constraint_in_place()
    {
        using var legacy = new LegacyNamedContext(new DbContextOptionsBuilder<LegacyNamedContext>().UseRaskPostgresAt(OfflinePostgres).Options);
        using var named = new RaskNamedContext(new DbContextOptionsBuilder<RaskNamedContext>().UseRaskPostgresAt(OfflinePostgres).Options);

        var operations = Differences(legacy, named);
        var sql = operations.OfType<SqlOperation>().Select(static s => s.Sql).ToList();

        Assert.All(operations, o => Assert.Contains(o.GetType(), Renames));
        Assert.Contains("ALTER TABLE fuelings RENAME CONSTRAINT \"Fueling_pkey\" TO pk_fuelings;", sql);
        Assert.Contains(sql, s => Renamed(s, "ALTER TABLE fuelings RENAME CONSTRAINT \"FK_Fueling_Pistol", "TO fk_fuelings_pistol_id;"));
        Assert.Contains(operations, o => o is RenameIndexOperation { Name: "Fueling_createdAt_idx", NewName: "ix_fuelings_created_at" });
    }

    [Fact]
    public void Going_back_to_the_old_names_is_renames_too()
    {
        using var legacy = new LegacyNamedContext(new DbContextOptionsBuilder<LegacyNamedContext>().UseRaskPostgresAt(OfflinePostgres).Options);
        using var named = new RaskNamedContext(new DbContextOptionsBuilder<RaskNamedContext>().UseRaskPostgresAt(OfflinePostgres).Options);

        var operations = Differences(named, legacy);

        Assert.All(operations, o => Assert.Contains(o.GetType(), Renames));
        Assert.NotEmpty(operations);
    }

    private static readonly Type[] SqliteRenames = [typeof(RenameTableOperation), typeof(RenameColumnOperation), typeof(RenameIndexOperation)];

    private static readonly Type[] Renames =
        [typeof(RenameTableOperation), typeof(RenameColumnOperation), typeof(RenameIndexOperation), typeof(SqlOperation)];

    private static bool Renamed(string sql, string from, string to) =>
        sql.Contains(from, StringComparison.Ordinal) && sql.Contains(to, StringComparison.Ordinal);

    private static List<MigrationOperation> Differences(DbContext from, DbContext to) =>
        [.. to.GetService<IMigrationsModelDiffer>().GetDifferences(
            from.GetService<IDesignTimeModel>().Model.GetRelationalModel(),
            to.GetService<IDesignTimeModel>().Model.GetRelationalModel())];
}
