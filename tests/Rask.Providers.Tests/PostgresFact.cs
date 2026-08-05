namespace Rask.Providers.Tests;

/// <summary>
/// A fact that needs a real PostgreSQL server, named by <c>RASK_PG_TEST_DB</c>.
/// </summary>
/// <remarks>
/// Skips rather than fails when the variable is absent, so running the suite without a server reports
/// SKIPPED instead of a wall of red — the same bargain the deploy-host gate makes with Docker. xunit here is
/// 2.9.3, which has no <c>Assert.Skip</c>, so the skip has to be decided in the attribute's constructor.
/// <c>scripts/run-providers-local.sh</c> starts the container and sets the variable.
/// </remarks>
public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Postgres.ConnectionString))
        {
            Skip = "Needs a PostgreSQL server: run scripts/run-providers-local.sh (or set RASK_PG_TEST_DB).";
        }
    }
}

/// <summary>The server under test, and a fresh schema per test class so classes can't collide.</summary>
internal static class Postgres
{
    public static string? ConnectionString => Environment.GetEnvironmentVariable("RASK_PG_TEST_DB");

    public static string Required =>
        ConnectionString ?? throw new InvalidOperationException("RASK_PG_TEST_DB is not set.");
}
