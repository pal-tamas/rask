using MySql.Data.MySqlClient;

namespace Rask.Providers.E2E.Tests;

/// <summary>The MySQL server under test, named by <c>RASK_MYSQL_TEST_DB</c>.</summary>
/// <remarks>
/// Named <c>MySqlServer</c>, not <c>MySql</c>: inside a <c>Rask.*</c> namespace a bare <c>MySql</c> would bind to
/// the <c>Rask.MySql</c> namespace this suite also references. Each test class works in a database of its own,
/// created and dropped by EF, whose name is always one of ours.
/// </remarks>
internal static class MySqlServer
{
    internal const string SkipReason =
        "Needs a MySQL server: run scripts/run-providers-local.sh, or set RASK_MYSQL_TEST_DB.";

    internal static string? ConnectionString => Environment.GetEnvironmentVariable("RASK_MYSQL_TEST_DB");

    internal static bool Available => !string.IsNullOrWhiteSpace(ConnectionString);

    /// <summary>The server's connection string, pointed at <paramref name="database"/>.</summary>
    internal static string Database(string database) =>
        new MySqlConnectionStringBuilder(ConnectionString ?? throw new InvalidOperationException("RASK_MYSQL_TEST_DB is not set."))
        {
            Database = database,
        }.ConnectionString;
}

/// <summary>Every MySQL class runs alone: they share one server, and the races are the point.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class MySqlCollection
{
    public const string Name = "mysql";
}
