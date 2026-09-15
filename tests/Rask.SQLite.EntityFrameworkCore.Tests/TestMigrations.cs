using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Rask.SQLite.EntityFrameworkCore.Tests;

/// <summary>
/// Runs a model through the differ and the migrations SQL generator — the same path <c>dotnet ef database update</c>
/// takes — against a real database, so the DDL under test is the DDL an app would get.
/// </summary>
internal static class TestMigrations
{
    /// <summary>Migrates <paramref name="context"/>'s database from <paramref name="from"/>'s model (or from nothing).</summary>
    /// <returns>The operations the differ produced, for tests that pin what a model change amounts to.</returns>
    public static IReadOnlyList<MigrationOperation> Apply(DbContext context, DbContext? from = null)
    {
        var target = context.GetService<IDesignTimeModel>().Model;
        var source = from?.GetService<IDesignTimeModel>().Model.GetRelationalModel();

        var operations = context.GetService<IMigrationsModelDiffer>()
            .GetDifferences(source, target.GetRelationalModel());

        foreach (var command in context.GetService<IMigrationsSqlGenerator>().Generate(operations, target))
        {
            context.Database.ExecuteSqlRaw(command.CommandText);
        }

        return operations;
    }

    /// <summary>Every schema object's SQL, one per line.</summary>
    public static string Ddl(DbContext context)
        => string.Join(
            "\n",
            context.Database.SqlQueryRaw<string>("SELECT COALESCE(sql, '') AS Value FROM sqlite_master").ToList());

    /// <summary>How many triggers the database holds.</summary>
    public static int TriggerCount(DbContext context)
        => context.Database
            .SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM sqlite_master WHERE type = 'trigger'")
            .AsEnumerable()
            .Single();
}
