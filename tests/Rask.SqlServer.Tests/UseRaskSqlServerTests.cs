using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Rask.SqlServer.Tests;

/// <summary>
/// What <c>UseRaskSqlServer</c> configures, without needing a server: building the options is entirely
/// offline. The behaviour that genuinely needs one lives in the opt-in provider suite.
/// </summary>
public class UseRaskSqlServerTests
{
    private const string ConnectionString = "Server=localhost;Database=rask;User Id=sa;Password=x;TrustServerCertificate=true";

    [Fact]
    public void UseRaskSqlServer_SelectsTheSqlServerProvider()
    {
        var options = new DbContextOptionsBuilder().UseRaskSqlServer(ConnectionString).Options;

        Assert.Contains(
            options.Extensions,
            extension => extension.GetType().FullName?.Contains("SqlServer", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void UseRaskSqlServer_RegistersTheConnectionInterceptor()
    {
        var options = new DbContextOptionsBuilder().UseRaskSqlServer(ConnectionString).Options;

        var interceptors = options.FindExtension<CoreOptionsExtension>()?.Interceptors ?? [];
        Assert.Contains(interceptors, interceptor => interceptor is RaskSqlServerConnectionInterceptor);
    }

    [Fact]
    public void UseRaskSqlServer_AppliesTheConfigureDelegate()
    {
        RaskSqlServerOptions? captured = null;

        new DbContextOptionsBuilder().UseRaskSqlServer(ConnectionString, p =>
        {
            p.CommandTimeout = TimeSpan.FromSeconds(5);
            p.LockTimeout = TimeSpan.FromSeconds(1);
            captured = p;
        });

        Assert.NotNull(captured);
        Assert.Equal(TimeSpan.FromSeconds(5), captured.CommandTimeout);
        Assert.Equal(TimeSpan.FromSeconds(1), captured.LockTimeout);
    }

    [Fact]
    public void UseRaskSqlServer_ValidatesTheConfiguredOptions()
    {
        Assert.Throws<InvalidOperationException>(() =>
            new DbContextOptionsBuilder().UseRaskSqlServer(ConnectionString, p => p.LockTimeout = TimeSpan.FromMinutes(5)));
    }

    [Fact]
    public void ASubSecondCommandTimeout_RoundsUpRatherThanToZero()
    {
        // SqlClient takes whole seconds and treats 0 as "wait forever" — truncating would turn the shortest
        // timeout anyone could ask for into no timeout at all.
        var options = new DbContextOptionsBuilder()
            .UseRaskSqlServer(ConnectionString, p =>
            {
                p.CommandTimeout = TimeSpan.FromMilliseconds(400);
                p.LockTimeout = TimeSpan.FromMilliseconds(100);
            })
            .Options;

        var extension = options.Extensions.Single(e => e.GetType().Name.Contains("SqlServer", StringComparison.Ordinal));
        var commandTimeout = (int?)extension.GetType().GetProperty("CommandTimeout")?.GetValue(extension);

        Assert.Equal(1, commandTimeout);
    }

    [Fact]
    public void UseRaskSqlServer_RejectsAnEmptyConnectionString()
    {
        Assert.Throws<ArgumentException>(() => new DbContextOptionsBuilder().UseRaskSqlServer(""));
    }

    [Fact]
    public void UseRaskSqlServer_RejectsANullBuilder()
    {
        Assert.Throws<ArgumentNullException>(() =>
            ((DbContextOptionsBuilder)null!).UseRaskSqlServer(ConnectionString));
    }

    [Fact]
    public void TheGenericOverload_KeepsTheTypedOptions()
    {
        var options = new DbContextOptionsBuilder<TestContext>().UseRaskSqlServer(ConnectionString).Options;

        Assert.IsType<DbContextOptions<TestContext>>(options, exactMatch: false);
        var interceptors = options.FindExtension<CoreOptionsExtension>()?.Interceptors ?? [];
        Assert.Contains(interceptors, interceptor => interceptor is RaskSqlServerConnectionInterceptor);
    }

    [Fact]
    public void TheInterceptor_RejectsNullOptions()
    {
        Assert.Throws<ArgumentNullException>(() => new RaskSqlServerConnectionInterceptor(null!));
    }

    [Fact]
    public void TheInterceptor_IgnoresANonSqlServerConnection()
    {
        var interceptor = new RaskSqlServerConnectionInterceptor(new RaskSqlServerOptions());

        Assert.Null(Record.Exception(() => interceptor.ConnectionOpened(new NotSqlServerConnection(), null!)));
    }

    private sealed class TestContext : DbContext
    {
        public TestContext(DbContextOptions<TestContext> options)
            : base(options)
        {
        }
    }

    /// <summary>A connection from some other provider. Every member throws: the interceptor must not touch it.</summary>
    private sealed class NotSqlServerConnection : System.Data.Common.DbConnection
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
