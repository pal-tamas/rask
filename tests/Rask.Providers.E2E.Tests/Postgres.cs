using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace Rask.Providers.E2E.Tests;

/// <summary>The PostgreSQL server under test: <c>RASK_PG_TEST_DB</c>, or the run's container (<see cref="ProviderServer" />).</summary>
/// <remarks>
/// A fact that needs it calls <c>Assert.SkipUnless(Postgres.Available, Postgres.SkipReason)</c> first, so a run with no
/// server reports SKIPPED instead of a wall of red — and never PASSED, which is the lie that matters.
/// </remarks>
internal static class Postgres
{
    internal static string SkipReason => ProviderServer.Postgres.SkipReason ?? "";

    internal static bool Available => ProviderServer.Postgres.Available;

    /// <summary>The server's connection string; the container is started on the first call.</summary>
    internal static string Required => ProviderServer.Postgres.ConnectionString;

    /// <summary>Drops and recreates <paramref name="schema"/>, then creates the context's tables in it.</summary>
    /// <remarks>
    /// Not <c>EnsureCreated</c>: it does nothing on a database that already holds tables in any schema, so the
    /// second test class would silently run against no tables at all. <c>CreateTables</c> does what it says.
    /// </remarks>
    internal static async Task ResetSchemaAsync(DbContext db, string schema)
    {
        await DropSchemaAsync(db, schema);

        // A schema name is an identifier, so it cannot be a parameter. Every caller passes a constant.
#pragma warning disable EF1002
        await db.Database.ExecuteSqlRawAsync($"CREATE SCHEMA \"{schema}\";");
#pragma warning restore EF1002

        await db.GetService<IRelationalDatabaseCreator>().CreateTablesAsync();
    }

    internal static async Task DropSchemaAsync(DbContext db, string schema)
    {
#pragma warning disable EF1002 // Identifier, not a value — see ResetSchemaAsync.
        await db.Database.ExecuteSqlRawAsync($"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE;");
#pragma warning restore EF1002
    }
}

/// <summary>Every PostgreSQL class runs alone: they share one server, and the races are the point.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PostgresCollection
{
    public const string Name = "postgres";
}
