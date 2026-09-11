using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Rask.MySql.Tests;

/// <summary>
/// What <c>UseRaskMySql</c> configures, checked without a server: building options is offline. What genuinely needs
/// one lives in <c>Rask.Providers.E2E.Tests</c>.
/// </summary>
public sealed class UseRaskMySqlTests
{
    private const string ConnectionString = "Server=localhost;Database=rask;User ID=rask;Password=rask";

    [Fact]
    public void It_selects_Oracles_MySQL_provider()
    {
        using var db = new PlainContext(Options());

        Assert.Equal("MySql.EntityFrameworkCore", db.Database.ProviderName);
    }

    [Fact]
    public void It_sets_the_client_command_timeout_in_whole_seconds_rounded_up()
    {
        var options = Options(m =>
        {
            m.CommandTimeout = TimeSpan.FromMilliseconds(1_400);
            m.LockTimeout = TimeSpan.FromMilliseconds(500);
            m.StatementTimeout = TimeSpan.Zero;
        });

        Assert.Equal(2, RelationalOptionsExtension.Extract(options).CommandTimeout);
    }

    [Fact]
    public void It_registers_the_connection_interceptor()
    {
        var interceptors = Options().FindExtension<CoreOptionsExtension>()?.Interceptors ?? [];

        Assert.Single(interceptors, interceptor => interceptor is RaskMySqlConnectionInterceptor);
    }

    [Fact]
    public void Calling_it_twice_keeps_one_interceptor_and_the_last_calls_settings()
    {
        var builder = new DbContextOptionsBuilder<PlainContext>();
        builder.UseRaskMySql(ConnectionString);
        builder.UseRaskMySql(ConnectionString, m =>
        {
            m.StatementTimeout = TimeSpan.Zero;
            m.LockTimeout = TimeSpan.Zero;
        });

        var interceptors = builder.Options.FindExtension<CoreOptionsExtension>()?.Interceptors ?? [];
        Assert.Single(interceptors, interceptor => interceptor is RaskMySqlConnectionInterceptor);

        using var db = new PlainContext(builder.Options);
        var effective = RaskMySqlConnectionInterceptor.OptionsFor(db);
        Assert.NotNull(effective);
        Assert.Empty(MySqlSessionSettings.BuildScript(effective));
    }

    [Fact]
    public void It_validates_the_configured_options()
    {
        Assert.Throws<InvalidOperationException>(() => Options(m => m.LockTimeout = TimeSpan.FromMinutes(5)));
    }

    [Fact]
    public void Retrying_is_on_by_default()
    {
        using var db = new PlainContext(Options());

        Assert.True(db.Database.CreateExecutionStrategy().RetriesOnFailure);
    }

    [Fact]
    public void Retrying_can_be_turned_off()
    {
        using var db = new PlainContext(Options(m => m.Retry.Enabled = false));

        Assert.False(db.Database.CreateExecutionStrategy().RetriesOnFailure);
    }

    [Fact]
    public void It_rejects_an_empty_connection_string()
    {
        Assert.Throws<ArgumentException>(() => new DbContextOptionsBuilder().UseRaskMySql(""));
    }

    [Fact]
    public void It_rejects_a_null_builder()
    {
        Assert.Throws<ArgumentNullException>(() => ((DbContextOptionsBuilder)null!).UseRaskMySql(ConnectionString));
    }

    [Fact]
    public void The_generic_overload_keeps_the_typed_options()
    {
        var options = new DbContextOptionsBuilder<PlainContext>().UseRaskMySql(ConnectionString).Options;

        Assert.IsType<DbContextOptions<PlainContext>>(options, exactMatch: false);
    }

    [Fact]
    public async Task The_interceptor_leaves_another_providers_connection_alone()
    {
        var interceptor = new RaskMySqlConnectionInterceptor();

        Assert.Null(Record.Exception(() => interceptor.ConnectionOpened(new NotMySqlConnection(), null!)));
        Assert.Null(await Record.ExceptionAsync(() => interceptor.ConnectionOpenedAsync(new NotMySqlConnection(), null!)));
    }

    private static DbContextOptions<PlainContext> Options(Action<MySqlOptions>? configure = null) =>
        new DbContextOptionsBuilder<PlainContext>().UseRaskMySql(ConnectionString, configure).Options;

    private sealed class PlainContext(DbContextOptions<PlainContext> options) : DbContext(options);

    /// <summary>A connection from some other provider. Every member throws: the interceptor must not touch it.</summary>
    private sealed class NotMySqlConnection : DbConnection
    {
        [AllowNull]
        public override string ConnectionString { get => ""; set { } }

        public override string Database => throw new NotSupportedException();

        public override string DataSource => throw new NotSupportedException();

        public override string ServerVersion => throw new NotSupportedException();

        public override ConnectionState State => ConnectionState.Open;

        public override void ChangeDatabase(string databaseName) => throw new NotSupportedException();

        public override void Close() => throw new NotSupportedException();

        public override void Open() => throw new NotSupportedException();

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) =>
            throw new NotSupportedException();

        protected override DbCommand CreateDbCommand() => throw new NotSupportedException();
    }
}
