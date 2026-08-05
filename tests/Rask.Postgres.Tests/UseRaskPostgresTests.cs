using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Rask.Postgres.Tests;

/// <summary>
/// Covers what <c>UseRaskPostgres</c> configures without needing a PostgreSQL server: building the options
/// is entirely offline, so these assert the wiring rather than the round trip. The behaviour that genuinely
/// needs a server lives in the opt-in provider suite.
/// </summary>
public class UseRaskPostgresTests
{
    private const string ConnectionString = "Host=localhost;Database=rask;Username=rask;Password=rask";

    [Fact]
    public void UseRaskPostgres_SelectsTheNpgsqlProvider()
    {
        var options = new DbContextOptionsBuilder().UseRaskPostgres(ConnectionString).Options;

        Assert.Contains(
            options.Extensions,
            extension => extension.GetType().FullName?.Contains("Npgsql", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void UseRaskPostgres_RegistersTheConnectionInterceptor()
    {
        var options = new DbContextOptionsBuilder().UseRaskPostgres(ConnectionString).Options;

        var interceptors = options.FindExtension<CoreOptionsExtension>()?.Interceptors ?? [];
        Assert.Contains(interceptors, interceptor => interceptor is RaskPostgresConnectionInterceptor);
    }

    [Fact]
    public void UseRaskPostgres_AppliesTheConfigureDelegate()
    {
        RaskPostgresOptions? captured = null;

        new DbContextOptionsBuilder().UseRaskPostgres(ConnectionString, p =>
        {
            p.StatementTimeout = TimeSpan.FromSeconds(5);
            p.LockTimeout = TimeSpan.FromSeconds(1);
            captured = p;
        });

        Assert.NotNull(captured);
        Assert.Equal(TimeSpan.FromSeconds(5), captured.StatementTimeout);
        Assert.Equal(TimeSpan.FromSeconds(1), captured.LockTimeout);
    }

    [Fact]
    public void UseRaskPostgres_ValidatesTheConfiguredOptions()
    {
        // Validation has to run inside UseRaskPostgres, not just when someone calls Validate by hand —
        // otherwise a contradictory pair of timeouts only surfaces as confusing behaviour in production.
        Assert.Throws<InvalidOperationException>(() =>
            new DbContextOptionsBuilder().UseRaskPostgres(ConnectionString, p => p.LockTimeout = TimeSpan.FromMinutes(5)));
    }

    [Fact]
    public void UseRaskPostgres_RejectsAnEmptyConnectionString()
    {
        Assert.Throws<ArgumentException>(() => new DbContextOptionsBuilder().UseRaskPostgres(""));
    }

    [Fact]
    public void UseRaskPostgres_RejectsANullBuilder()
    {
        Assert.Throws<ArgumentNullException>(() =>
            ((DbContextOptionsBuilder)null!).UseRaskPostgres(ConnectionString));
    }

    [Fact]
    public void TheGenericOverload_KeepsTheTypedOptions()
    {
        var options = new DbContextOptionsBuilder<TestContext>().UseRaskPostgres(ConnectionString).Options;

        Assert.IsType<DbContextOptions<TestContext>>(options, exactMatch: false);
        var interceptors = options.FindExtension<CoreOptionsExtension>()?.Interceptors ?? [];
        Assert.Contains(interceptors, interceptor => interceptor is RaskPostgresConnectionInterceptor);
    }

    [Fact]
    public void TheInterceptor_RejectsNullOptions()
    {
        Assert.Throws<ArgumentNullException>(() => new RaskPostgresConnectionInterceptor(null!));
    }

    [Fact]
    public void TheInterceptor_IgnoresANonPostgresConnection()
    {
        // The interceptor is registered on the context, and a test double or another provider's connection
        // must pass through untouched rather than throwing a cast exception.
        var interceptor = new RaskPostgresConnectionInterceptor(new RaskPostgresOptions());

        var exception = Record.Exception(() => interceptor.ConnectionOpened(new NotPostgresConnection(), null!));

        Assert.Null(exception);
    }

    [Fact]
    public async Task TheInterceptor_IgnoresANonPostgresConnectionAsynchronously()
    {
        var interceptor = new RaskPostgresConnectionInterceptor(new RaskPostgresOptions());

        var exception = await Record.ExceptionAsync(() =>
            interceptor.ConnectionOpenedAsync(new NotPostgresConnection(), null!));

        Assert.Null(exception);
    }

    private sealed class TestContext : DbContext
    {
        public TestContext(DbContextOptions<TestContext> options)
            : base(options)
        {
        }
    }

    /// <summary>A connection from some other provider. Every member throws: the interceptor must not touch it.</summary>
    private sealed class NotPostgresConnection : System.Data.Common.DbConnection
    {
        [System.Diagnostics.CodeAnalysis.AllowNull]
        public override string ConnectionString { get => ""; set { } }

        public override string Database => throw new NotSupportedException();

        public override string DataSource => throw new NotSupportedException();

        public override string ServerVersion => throw new NotSupportedException();

        public override System.Data.ConnectionState State => System.Data.ConnectionState.Open;

        public override void ChangeDatabase(string databaseName) => throw new NotSupportedException();

        public override void Close() => throw new NotSupportedException();

        public override void Open() => throw new NotSupportedException();

        protected override System.Data.Common.DbTransaction BeginDbTransaction(System.Data.IsolationLevel isolationLevel)
            => throw new NotSupportedException();

        protected override System.Data.Common.DbCommand CreateDbCommand() => throw new NotSupportedException();
    }
}
