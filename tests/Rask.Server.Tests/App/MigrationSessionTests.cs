namespace Rask.Server.Tests.App;

/// <summary>A start's migrations are not held to the timeouts made for an app's requests.</summary>
public sealed class MigrationSessionTests
{
    [Fact]
    public void A_postgres_migration_lifts_the_statement_and_lock_timeouts()
    {
        var lift = MigrationSession.LiftLimits("Npgsql.EntityFrameworkCore.PostgreSQL");

        Assert.Equal("SET statement_timeout = 0; SET lock_timeout = 0;", lift);
    }

    [Fact]
    public void A_sql_server_migration_waits_for_locks_without_a_limit()
    {
        var lift = MigrationSession.LiftLimits("Microsoft.EntityFrameworkCore.SqlServer");

        Assert.Equal("SET LOCK_TIMEOUT -1;", lift);
    }

    [Fact]
    public void A_sqlite_migration_has_nothing_to_lift()
    {
        var lift = MigrationSession.LiftLimits("Microsoft.EntityFrameworkCore.Sqlite");

        Assert.Null(lift);
    }
}
