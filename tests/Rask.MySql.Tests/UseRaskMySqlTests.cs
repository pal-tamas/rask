using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Rask.MySql.Tests;

/// <summary>
/// What <c>UseRaskMySql</c> configures, checked without a server: building options is offline. What genuinely needs
/// one lives in <c>Rask.Providers.E2E.Tests</c>.
/// </summary>
public sealed class UseRaskMySqlTests
{
    private const string ConnectionString = "Server=localhost;Database=rask;User ID=rask;Password=rask";

    private static readonly IServiceProvider Services = ServicesWith(new() { ["Rask:ConnectionStrings:App"] = ConnectionString });

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
    public void The_Rask_MySql_section_sets_the_timeouts()
    {
        var services = ServicesWith(new()
        {
            ["Rask:ConnectionStrings:App"] = ConnectionString,
            ["Rask:MySql:CommandTimeout"] = "00:00:12",
            ["Rask:MySql:LockTimeout"] = "00:00:03",
        });

        var options = new DbContextOptionsBuilder<PlainContext>().UseRaskMySql(services).Options;

        Assert.Equal(12, RelationalOptionsExtension.Extract(options).CommandTimeout);
        using var db = new PlainContext(options);
        var effective = RaskMySqlConnectionInterceptor.OptionsFor(db);
        Assert.NotNull(effective);
        Assert.Equal(TimeSpan.FromSeconds(3), effective.LockTimeout);
    }

    [Fact]
    public void The_configure_delegate_wins_over_the_section()
    {
        var services = ServicesWith(new()
        {
            ["Rask:ConnectionStrings:App"] = ConnectionString,
            ["Rask:MySql:Retry:Enabled"] = "true",
        });

        using var db = new PlainContext(
            new DbContextOptionsBuilder<PlainContext>().UseRaskMySql(services, m => m.Retry.Enabled = false).Options);

        Assert.False(db.Database.CreateExecutionStrategy().RetriesOnFailure);
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
        builder.UseRaskMySql(Services);
        builder.UseRaskMySql(Services, m =>
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
        // It names the section, because the value may just as well have come from appsettings as from the callback.
        var error = Assert.Throws<OptionsValidationException>(() => Options(m => m.LockTimeout = TimeSpan.FromMinutes(5)));

        Assert.Contains("Rask:MySql", error.Message, StringComparison.Ordinal);
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
    public void It_names_the_connection_string_it_could_not_find()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => new DbContextOptionsBuilder().UseRaskMySql(ServicesWith([])));

        Assert.Contains("Rask:ConnectionStrings:App", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_top_level_connection_string_is_not_read()
    {
        Assert.Throws<InvalidOperationException>(() =>
            new DbContextOptionsBuilder().UseRaskMySql(ServicesWith(new() { ["ConnectionStrings:App"] = ConnectionString })));
    }

    [Fact]
    public void It_rejects_a_null_builder()
    {
        Assert.Throws<ArgumentNullException>(() => ((DbContextOptionsBuilder)null!).UseRaskMySql(Services));
    }

    [Fact]
    public void The_generic_overload_keeps_the_typed_options()
    {
        var options = new DbContextOptionsBuilder<PlainContext>().UseRaskMySql(Services).Options;

        Assert.IsType<DbContextOptions<PlainContext>>(options, exactMatch: false);
    }

    [Fact]
    public async Task The_interceptor_leaves_another_providers_connection_alone()
    {
        var interceptor = new RaskMySqlConnectionInterceptor();

        Assert.Null(Record.Exception(() => interceptor.ConnectionOpened(new NotMySqlConnection(), null!)));
        Assert.Null(await Record.ExceptionAsync(() => interceptor.ConnectionOpenedAsync(new NotMySqlConnection(), null!)));
    }

    internal static IServiceProvider ServicesWith(Dictionary<string, string?> settings) =>
        new ConfigurationServices(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());

    private static DbContextOptions<PlainContext> Options(Action<MySqlOptions>? configure = null) =>
        new DbContextOptionsBuilder<PlainContext>().UseRaskMySql(Services, configure).Options;

    private sealed class PlainContext(DbContextOptions<PlainContext> options) : DbContext(options);

    // Just the configuration: that is all UseRaskMySql asks the provider for.
    private sealed class ConfigurationServices(IConfiguration configuration) : IServiceProvider
    {
        public object? GetService(Type serviceType) => serviceType == typeof(IConfiguration) ? configuration : null;
    }

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
