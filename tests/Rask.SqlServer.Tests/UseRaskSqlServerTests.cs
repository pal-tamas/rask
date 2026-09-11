using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Rask.Cache;

namespace Rask.SqlServer.Tests;

/// <summary>
/// What <c>UseRaskSqlServer</c> configures, checked without a server: building options and the model is offline.
/// What genuinely needs one lives in <c>Rask.Providers.E2E.Tests</c>.
/// </summary>
public sealed class UseRaskSqlServerTests
{
    private const string ConnectionString = "Server=localhost;Database=rask;User Id=sa;Password=x;TrustServerCertificate=true";

    private static readonly IServiceProvider Services = ServicesWith(new() { ["Rask:ConnectionStrings:App"] = ConnectionString });

    [Fact]
    public void It_selects_the_SQL_Server_provider()
    {
        using var db = new PlainContext(Options<PlainContext>());

        Assert.Equal("Microsoft.EntityFrameworkCore.SqlServer", db.Database.ProviderName);
    }

    [Fact]
    public void It_sets_the_client_command_timeout_in_seconds()
    {
        var options = Options<PlainContext>(s =>
        {
            s.CommandTimeout = TimeSpan.FromSeconds(12);
            s.LockTimeout = TimeSpan.FromSeconds(1);
        });

        Assert.Equal(12, RelationalOptionsExtension.Extract(options).CommandTimeout);
    }

    [Fact]
    public void A_sub_second_command_timeout_rounds_up_rather_than_to_wait_forever()
    {
        var options = Options<PlainContext>(s =>
        {
            s.CommandTimeout = TimeSpan.FromMilliseconds(400);
            s.LockTimeout = TimeSpan.FromMilliseconds(100);
        });

        Assert.Equal(1, RelationalOptionsExtension.Extract(options).CommandTimeout);
    }

    [Fact]
    public void The_Rask_SqlServer_section_sets_the_timeouts()
    {
        var services = ServicesWith(new()
        {
            ["Rask:ConnectionStrings:App"] = ConnectionString,
            ["Rask:SqlServer:CommandTimeout"] = "00:00:12",
            ["Rask:SqlServer:LockTimeout"] = "00:00:03",
        });

        var options = new DbContextOptionsBuilder<PlainContext>().UseRaskSqlServer(services).Options;

        Assert.Equal(12, RelationalOptionsExtension.Extract(options).CommandTimeout);
        using var db = new PlainContext(options);
        var effective = RaskSqlServerConnectionInterceptor.OptionsFor(db);
        Assert.NotNull(effective);
        Assert.Equal("SET XACT_ABORT ON;SET LOCK_TIMEOUT 3000;", SqlServerSessionSettings.BuildScript(effective));
    }

    [Fact]
    public void The_configure_delegate_wins_over_the_section()
    {
        var services = ServicesWith(new()
        {
            ["Rask:ConnectionStrings:App"] = ConnectionString,
            ["Rask:SqlServer:Retry:Enabled"] = "true",
        });

        using var db = new PlainContext(
            new DbContextOptionsBuilder<PlainContext>().UseRaskSqlServer(services, s => s.Retry.Enabled = false).Options);

        Assert.False(db.Database.CreateExecutionStrategy().RetriesOnFailure);
    }

    [Fact]
    public void It_registers_the_connection_interceptor()
    {
        var options = Options<PlainContext>();

        var interceptors = options.FindExtension<CoreOptionsExtension>()?.Interceptors ?? [];
        Assert.Single(interceptors, interceptor => interceptor is RaskSqlServerConnectionInterceptor);
    }

    [Fact]
    public void It_validates_the_configured_options()
    {
        // It names the section, because the value may just as well have come from appsettings as from the callback.
        var error = Assert.Throws<OptionsValidationException>(
            () => Options<PlainContext>(s => s.LockTimeout = TimeSpan.FromMinutes(5)));

        Assert.Contains("Rask:SqlServer", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Retrying_is_on_by_default()
    {
        using var db = new PlainContext(Options<PlainContext>());

        Assert.True(db.Database.CreateExecutionStrategy().RetriesOnFailure);
    }

    [Fact]
    public void Retrying_can_be_turned_off()
    {
        using var db = new PlainContext(Options<PlainContext>(s => s.Retry.Enabled = false));

        Assert.False(db.Database.CreateExecutionStrategy().RetriesOnFailure);
    }

    [Fact]
    public void It_names_the_connection_string_it_could_not_find()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => new DbContextOptionsBuilder().UseRaskSqlServer(ServicesWith([])));

        Assert.Contains("Rask:ConnectionStrings:App", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_top_level_connection_string_is_not_read()
    {
        Assert.Throws<InvalidOperationException>(() =>
            new DbContextOptionsBuilder().UseRaskSqlServer(ServicesWith(new() { ["ConnectionStrings:App"] = ConnectionString })));
    }

    [Fact]
    public void It_rejects_a_null_builder()
    {
        Assert.Throws<ArgumentNullException>(() => ((DbContextOptionsBuilder)null!).UseRaskSqlServer(Services));
    }

    [Fact]
    public void The_generic_overload_keeps_the_typed_options()
    {
        var options = new DbContextOptionsBuilder<PlainContext>().UseRaskSqlServer(Services).Options;

        Assert.IsType<DbContextOptions<PlainContext>>(options, exactMatch: false);
    }

    [Fact]
    public void The_cache_key_is_capped_at_SQL_Servers_index_key_limit()
    {
        using var db = new CacheContext(Options<CacheContext>());

        // Both the runtime model and the design-time model migrations are generated from.
        Assert.Equal(450, KeyLength(db.Model));
        Assert.Equal(450, KeyLength(db.GetService<IDesignTimeModel>().Model));
    }

    [Fact]
    public void Without_UseRaskSqlServer_the_cache_key_keeps_its_configured_length()
    {
        // The cap is the SQL Server package's alone: every other provider keeps the room, and a SQLite app gets
        // no migration out of it.
        using var db = new CacheContext(new DbContextOptionsBuilder<CacheContext>().UseSqlServer(ConnectionString).Options);

        Assert.Equal(512, KeyLength(db.Model));
    }

    [Fact]
    public void No_other_string_key_is_resized()
    {
        using var db = new CacheContext(Options<CacheContext>());

        Assert.Equal(600, db.Model.FindEntityType(typeof(WideKeyed))!.FindProperty(nameof(WideKeyed.Name))!.GetMaxLength());
    }

    [Fact]
    public void The_convention_names_the_real_cache_entry_type()
    {
        // The package matches by name to stay free of a Rask.Cache dependency. A rename there must fail here, not
        // silently leave SQL Server apps with a key they cannot insert into.
        Assert.Equal(CacheKeyLengthConvention.CacheEntryTypeName, typeof(CacheEntry).FullName);
    }

    [Fact]
    public void Calling_it_twice_keeps_one_interceptor_and_the_last_calls_settings()
    {
        // An app configures the context, then a test or an environment override configures it again. The override
        // must win — not stack a second interceptor that keeps sending the first call's SET on every open.
        var builder = new DbContextOptionsBuilder<PlainContext>();
        builder.UseRaskSqlServer(Services);
        builder.UseRaskSqlServer(Services, s =>
        {
            s.AbortOnError = false;
            s.LockTimeout = TimeSpan.Zero;
        });

        var interceptors = builder.Options.FindExtension<CoreOptionsExtension>()?.Interceptors ?? [];
        Assert.Single(interceptors, interceptor => interceptor is RaskSqlServerConnectionInterceptor);

        using var db = new PlainContext(builder.Options);
        var effective = RaskSqlServerConnectionInterceptor.OptionsFor(db);
        Assert.NotNull(effective);
        Assert.Empty(SqlServerSessionSettings.BuildScript(effective));
    }

    [Fact]
    public void The_interceptor_reads_the_configured_settings_off_the_context()
    {
        using var db = new PlainContext(Options<PlainContext>(s => s.LockTimeout = TimeSpan.FromSeconds(3)));

        var effective = RaskSqlServerConnectionInterceptor.OptionsFor(db);
        Assert.NotNull(effective);
        Assert.Equal("SET XACT_ABORT ON;SET LOCK_TIMEOUT 3000;", SqlServerSessionSettings.BuildScript(effective));
    }

    [Fact]
    public async Task The_interceptor_leaves_another_providers_connection_alone()
    {
        var interceptor = new RaskSqlServerConnectionInterceptor();

        Assert.Null(Record.Exception(() => interceptor.ConnectionOpened(new NotSqlServerConnection(), null!)));
        Assert.Null(await Record.ExceptionAsync(() => interceptor.ConnectionOpenedAsync(new NotSqlServerConnection(), null!)));
    }

    private static DbContextOptions<TContext> Options<TContext>(Action<SqlServerOptions>? configure = null)
        where TContext : DbContext =>
        new DbContextOptionsBuilder<TContext>().UseRaskSqlServer(Services, configure).Options;

    private static int? KeyLength(Microsoft.EntityFrameworkCore.Metadata.IModel model) =>
        model.FindEntityType(typeof(CacheEntry))!.FindProperty(nameof(CacheEntry.Key))!.GetMaxLength();

    private static IServiceProvider ServicesWith(Dictionary<string, string?> settings) =>
        new ConfigurationServices(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());

    // Just the configuration: that is all UseRaskSqlServer asks the provider for.
    private sealed class ConfigurationServices(IConfiguration configuration) : IServiceProvider
    {
        public object? GetService(Type serviceType) => serviceType == typeof(IConfiguration) ? configuration : null;
    }

    private sealed class PlainContext(DbContextOptions<PlainContext> options) : DbContext(options);

    public sealed class WideKeyed
    {
        public string Name { get; set; } = "";
    }

    private sealed class CacheContext(DbContextOptions<CacheContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.AddRaskCache();
            modelBuilder.Entity<WideKeyed>().HasKey(x => x.Name);
            modelBuilder.Entity<WideKeyed>().Property(x => x.Name).HasMaxLength(600);
        }
    }

    /// <summary>A connection from some other provider. Every member throws: the interceptor must not touch it.</summary>
    private sealed class NotSqlServerConnection : DbConnection
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
