using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;

namespace Rask.Postgres.Tests;

/// <summary>
/// What <c>UseRaskPostgres</c> configures, checked without a PostgreSQL server: building the options is
/// entirely offline, so these assert the wiring. What genuinely needs a server — the settings reaching the
/// session and surviving the pool — lives in <c>Rask.Providers.E2E.Tests</c>.
/// </summary>
public sealed class UseRaskPostgresTests
{
    private const string ConnectionString = "Host=localhost;Database=rask;Username=rask;Password=rask";

    [Fact]
    public void It_selects_the_Npgsql_provider()
    {
        using var db = Context(o => o.UseRaskPostgres(ConnectionString));

        Assert.Equal("Npgsql.EntityFrameworkCore.PostgreSQL", db.Database.ProviderName);
    }

    [Fact]
    public void The_session_timeouts_travel_in_the_connection_string()
    {
        using var db = Context(o => o.UseRaskPostgres(ConnectionString));

        var builder = new NpgsqlConnectionStringBuilder(db.Database.GetConnectionString());
        Assert.Equal(
            "-c statement_timeout=30000 -c lock_timeout=10000 -c idle_in_transaction_session_timeout=60000",
            builder.Options);
        Assert.Equal("rask", builder.Password);
    }

    [Fact]
    public void Nothing_runs_per_connection_open()
    {
        // The settings are startup parameters, so there is no interceptor sending a SET — and no extra round
        // trip on every query EF runs.
        var options = new DbContextOptionsBuilder().UseRaskPostgres(ConnectionString).Options;

        Assert.Empty(options.FindExtension<CoreOptionsExtension>()?.Interceptors ?? []);
    }

    [Fact]
    public void It_applies_the_configure_delegate()
    {
        using var db = Context(o => o.UseRaskPostgres(ConnectionString, p =>
        {
            p.StatementTimeout = TimeSpan.FromSeconds(5);
            p.LockTimeout = TimeSpan.FromSeconds(1);
            p.IdleInTransactionSessionTimeout = TimeSpan.Zero;
        }));

        Assert.Equal(
            "-c statement_timeout=5000 -c lock_timeout=1000",
            new NpgsqlConnectionStringBuilder(db.Database.GetConnectionString()).Options);
    }

    [Fact]
    public void It_validates_the_configured_options()
    {
        // Validation runs inside UseRaskPostgres, not only when someone calls Validate by hand — otherwise a
        // contradictory pair of timeouts surfaces as confusing behaviour in production.
        Assert.Throws<InvalidOperationException>(() =>
            new DbContextOptionsBuilder().UseRaskPostgres(ConnectionString, p => p.LockTimeout = TimeSpan.FromMinutes(5)));
    }

    [Fact]
    public void Retrying_is_on_by_default()
    {
        using var db = Context(o => o.UseRaskPostgres(ConnectionString));

        Assert.True(db.Database.CreateExecutionStrategy().RetriesOnFailure);
    }

    [Fact]
    public void Retrying_can_be_turned_off()
    {
        using var db = Context(o => o.UseRaskPostgres(ConnectionString, p => p.Retry.Enabled = false));

        Assert.False(db.Database.CreateExecutionStrategy().RetriesOnFailure);
    }

    [Fact]
    public void It_rejects_an_empty_connection_string()
    {
        Assert.Throws<ArgumentException>(() => new DbContextOptionsBuilder().UseRaskPostgres(""));
    }

    [Fact]
    public void It_rejects_a_null_builder()
    {
        Assert.Throws<ArgumentNullException>(() => ((DbContextOptionsBuilder)null!).UseRaskPostgres(ConnectionString));
    }

    [Fact]
    public void The_generic_overload_keeps_the_typed_options()
    {
        var options = new DbContextOptionsBuilder<TestContext>().UseRaskPostgres(ConnectionString).Options;

        Assert.IsType<DbContextOptions<TestContext>>(options, exactMatch: false);

        using var db = new TestContext(options);
        Assert.StartsWith("-c statement_timeout=30000", new NpgsqlConnectionStringBuilder(db.Database.GetConnectionString()).Options);
    }

    private static TestContext Context(Action<DbContextOptionsBuilder<TestContext>> configure)
    {
        var builder = new DbContextOptionsBuilder<TestContext>();
        configure(builder);
        return new TestContext(builder.Options);
    }

    private sealed class TestContext(DbContextOptions<TestContext> options) : DbContext(options);
}
