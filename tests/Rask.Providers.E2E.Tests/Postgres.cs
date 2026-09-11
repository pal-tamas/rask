using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace Rask.Providers.E2E.Tests;

/// <summary>The PostgreSQL server under test, named by <c>RASK_PG_TEST_DB</c>.</summary>
/// <remarks>
/// A fact that needs it calls <c>Skip.IfNot(Postgres.Available, Postgres.SkipReason)</c> first, so a run with no
/// server reports SKIPPED instead of a wall of red — and never PASSED, which is the lie that matters.
/// </remarks>
internal static class Postgres
{
    internal const string SkipReason =
        "Needs a PostgreSQL server: run scripts/run-providers-local.sh, or set RASK_PG_TEST_DB.";

    internal static string? ConnectionString => Environment.GetEnvironmentVariable("RASK_PG_TEST_DB");

    internal static bool Available => !string.IsNullOrWhiteSpace(ConnectionString);

    internal static string Required =>
        ConnectionString ?? throw new InvalidOperationException("RASK_PG_TEST_DB is not set.");

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
