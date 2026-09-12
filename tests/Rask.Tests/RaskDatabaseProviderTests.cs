using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Rask.Logging;

namespace Rask.Tests;

/// <summary>
///     <c>Rask:Database:Provider</c> picks the application database, and <c>RaskApp</c> wires the batteries for the
///     database it picked. Nothing here opens a connection: building options, a model and a service graph is offline.
/// </summary>
public sealed class RaskDatabaseProviderTests
{
    private const string PostgresApp = "Host=localhost;Database=rask;Username=rask;Password=rask";
    private const string SqlServerApp = "Server=localhost;Database=rask;User Id=rask;Password=rask;TrustServerCertificate=true";

    private sealed class PlainContext(DbContextOptions<PlainContext> options) : DbContext(options);

    /// <summary>An app context that maps the log table, as one on a server database has to.</summary>
    private sealed class LoggedContext(DbContextOptions<LoggedContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.AddRaskLogging();
    }

    [Theory]
    [InlineData(null, "Microsoft.EntityFrameworkCore.Sqlite", "Data Source=:memory:")]
    [InlineData("sqlite", "Microsoft.EntityFrameworkCore.Sqlite", "Data Source=:memory:")]
    [InlineData("postgres", "Npgsql.EntityFrameworkCore.PostgreSQL", PostgresApp)]
    [InlineData(" PostgreS ", "Npgsql.EntityFrameworkCore.PostgreSQL", PostgresApp)]
    [InlineData("sqlserver", "Microsoft.EntityFrameworkCore.SqlServer", SqlServerApp)]
    public void UseRaskDatabase_opens_the_provider_the_setting_names(string? provider, string expected, string connectionString)
    {
        using var services = Services(provider, connectionString);

        using var db = new PlainContext(new DbContextOptionsBuilder<PlainContext>().UseRaskDatabase(services).Options);

        Assert.Equal(expected, db.Database.ProviderName);
    }

    [Fact]
    public void A_provider_Rask_cannot_open_is_refused_naming_the_key_and_the_choices()
    {
        using var services = Services("mysql", "Server=localhost");

        var error = Assert.Throws<InvalidOperationException>(
            () => new DbContextOptionsBuilder<PlainContext>().UseRaskDatabase(services));

        Assert.Contains("Rask:Database:Provider is 'mysql'", error.Message, StringComparison.Ordinal);
        Assert.Contains("sqlite, postgres or sqlserver", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("postgres", PostgresApp, "Npgsql.EntityFrameworkCore.PostgreSQL")]
    [InlineData("sqlserver", SqlServerApp, "Microsoft.EntityFrameworkCore.SqlServer")]
    public void A_RaskApp_on_a_server_database_opens_it_and_keeps_the_log_there(
        string provider, string connectionString, string expected)
    {
        var built = Build(Settings(provider, connectionString));

        using var db = built.Services.GetRequiredService<IDbContextFactory<RaskAppDbContext>>().CreateDbContext();
        Assert.Equal(expected, db.Database.ProviderName);
        Assert.Contains(db.Model.GetEntityTypes(), e => e.GetTableName() == "RaskLog");
        Assert.Equal("DbContextLogStore`1", built.Services.GetRequiredService<ILogs>().GetType().Name);
        Assert.DoesNotContain(HostedServices(built), name => name == "SqliteSnapshotService");
    }

    [Fact]
    public void A_RaskApp_on_sqlite_is_unchanged()
    {
        var settings = new Dictionary<string, string?> { ["Rask:Litestream:ReplicaUrl"] = "s3://bucket/app" };
        var built = Build(settings);

        using var db = built.Services.GetRequiredService<IDbContextFactory<RaskAppDbContext>>().CreateDbContext();
        Assert.Equal("Microsoft.EntityFrameworkCore.Sqlite", db.Database.ProviderName);
        Assert.DoesNotContain(db.Model.GetEntityTypes(), e => e.GetTableName() == "RaskLog");
        Assert.Equal("SqliteLogStore", built.Services.GetRequiredService<ILogs>().GetType().Name);
        Assert.Equal("Data Source=app.db", built.Services.GetRequiredService<IConfiguration>()["Rask:ConnectionStrings:App"]);
        Assert.Contains(HostedServices(built), name => name == "SqliteSnapshotService");
        Assert.Contains(HostedServices(built), name => name == "LitestreamReplicationService");
    }

    /// <summary>
    ///     The SQLite default, "Data Source=app.db", is not added off SQLite: handed to a server driver it would fail
    ///     somewhere far less helpful than a message naming the key.
    /// </summary>
    [Fact]
    public void A_RaskApp_on_a_server_database_has_no_app_db_to_fall_back_to()
    {
        var built = Build(new() { ["Rask:Database:Provider"] = "postgres" });

        var error = Assert.ThrowsAny<Exception>(
            () => built.Services.GetRequiredService<IDbContextFactory<RaskAppDbContext>>().CreateDbContext());

        Assert.Contains("Rask:ConnectionStrings:App", Messages(error), StringComparison.Ordinal);
    }

    /// <summary>
    ///     Building a model proves it validates; generating the create script proves each provider can turn it into DDL —
    ///     every battery's table, Rask.Data's conventions and the log table included — without a server to run it on.
    /// </summary>
    [Theory]
    [InlineData("postgres", PostgresApp)]
    [InlineData("sqlserver", SqlServerApp)]
    public void Rask_s_own_database_generates_its_schema_on_a_server_provider(string provider, string connectionString)
    {
        var built = Build(Settings(provider, connectionString));

        using var db = built.Services.GetRequiredService<IDbContextFactory<RaskAppDbContext>>().CreateDbContext();
        var script = db.Database.GenerateCreateScript();

        Assert.Contains("RaskLog", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Snapshots_configured_in_appsettings_refuse_to_start_on_a_server_database()
    {
        var settings = ServerSettings();
        settings["Rask:Snapshots:Retain"] = "7";

        var error = Assert.Throws<InvalidOperationException>(() => Build(settings));

        Assert.Contains("Rask:Snapshots", error.Message, StringComparison.Ordinal);
        Assert.Contains("app.Configure(c => c.Snapshots.Off())", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Snapshots_configured_in_code_refuse_to_start_on_a_server_database()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => Build(ServerSettings(), app => app.Configure(c => c.Snapshots.Configure(o => o.Retain = 3))));

        Assert.Contains("Snapshots are configured, but Rask:Database:Provider is postgres", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Snapshots_turned_on_in_code_refuse_to_start_on_a_server_database()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => Build(ServerSettings(), app => app.Configure(c => c.Snapshots.On())));

        Assert.Contains("app.Configure(c => c.Snapshots.Off())", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Snapshots_turned_off_are_not_refused_even_when_configured()
    {
        var settings = ServerSettings();
        settings["Rask:Snapshots:Retain"] = "7";

        var built = Build(settings, app => app.Configure(c => c.Snapshots.Off()));

        Assert.DoesNotContain(HostedServices(built), name => name == "SqliteSnapshotService");
    }

    [Fact]
    public void A_litestream_replica_refuses_to_start_on_a_server_database()
    {
        var settings = ServerSettings();
        settings["Rask:Litestream:ReplicaUrl"] = "s3://bucket/app";

        var error = Assert.Throws<InvalidOperationException>(() => Build(settings));

        Assert.Contains("Rask:Litestream:ReplicaUrl", error.Message, StringComparison.Ordinal);
    }

    /// <summary>With the database battery off there is no application database, so nothing follows the provider.</summary>
    [Fact]
    public void With_the_database_off_a_server_provider_changes_nothing()
    {
        var settings = ServerSettings();
        settings["Rask:Litestream:ReplicaUrl"] = "s3://bucket/app";

        var built = Build(settings, app => app.Configure(c => c.Data.Off()));

        Assert.Equal("SqliteLogStore", built.Services.GetRequiredService<ILogs>().GetType().Name);
    }

    /// <summary>An app that wires a log store itself wins, as it does for every battery — and is not asked to map a table.</summary>
    [Fact]
    public void An_app_that_wires_its_own_log_store_keeps_it_on_a_server_database()
    {
        var app = CreateApp(ServerSettings());
        app.Services.AddRaskLogging();
        var built = app.Build<TestApp>();

        Assert.Equal("SqliteLogStore", built.Services.GetRequiredService<ILogs>().GetType().Name);
        Assert.DoesNotContain(HostedServices(built), name => name == "LogsModelCheck`1");
    }

    [Fact]
    public void An_app_context_on_a_server_database_gets_the_log_store_on_that_context()
    {
        var app = CreateApp(ServerSettings());
        app.Services.AddDbContextFactory<LoggedContext>((sp, o) => o.UseRaskDatabase(sp));
        var built = app.Build<TestApp>();

        Assert.Equal("DbContextLogStore`1", built.Services.GetRequiredService<ILogs>().GetType().Name);
        Assert.Contains(HostedServices(built), name => name == "LogsModelCheck`1");
    }

    /// <summary>An app context that never mapped the log table is told the line to add, like every other battery's table.</summary>
    [Fact]
    public async Task An_app_context_on_a_server_database_that_does_not_map_the_log_is_told_the_line()
    {
        var app = CreateApp(ServerSettings());
        app.Services.AddDbContextFactory<PlainContext>((sp, o) => o.UseRaskDatabase(sp));
        var built = app.Build<TestApp>();

        var check = built.Services.GetServices<IHostedService>().Single(s => s.GetType().Name == "LogsModelCheck`1");
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => check.StartAsync(CancellationToken.None));

        Assert.Contains("modelBuilder.AddRaskLogging();", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_app_context_opening_another_provider_than_the_setting_names_fails_its_check()
    {
        var app = CreateApp(ServerSettings());
        app.Services.AddDbContextFactory<PlainContext>(o => o.UseSqlite("Data Source=:memory:"));
        var built = app.Build<TestApp>();

        var error = Assert.Throws<InvalidOperationException>(() => VerifyProvider<PlainContext>(built));

        Assert.Contains("Rask:Database:Provider is postgres", error.Message, StringComparison.Ordinal);
        Assert.Contains("o.UseRaskDatabase(sp)", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_app_context_on_the_configured_provider_passes_its_check()
    {
        var app = CreateApp(settings: null);
        app.Services.AddDbContextFactory<PlainContext>(o => o.UseSqlite("Data Source=:memory:"));
        var built = app.Build<TestApp>();

        Assert.Null(Record.Exception(() => VerifyProvider<PlainContext>(built)));
    }

    /// <summary>
    ///     The provider check is validated before the batteries. With the key unset (SQLite) and the context on Npgsql,
    ///     the snapshot battery's own validation would also fail — on a path the server string does not name — and
    ///     reporting that first would hide the real mistake.
    /// </summary>
    [Fact]
    public void The_provider_mismatch_is_the_first_thing_start_up_reports()
    {
        var app = CreateApp(settings: null);
        app.Services.AddDbContextFactory<PlainContext>(o => o.UseNpgsql(PostgresApp));
        var built = app.Build<TestApp>();

        var error = Assert.ThrowsAny<Exception>(() => built.Services.GetRequiredService<IStartupValidator>().Validate());

        Assert.IsType<InvalidOperationException>(error);
        Assert.Contains("Rask:Database:Provider is sqlite", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Rask_s_own_context_needs_no_provider_check()
    {
        var built = Build(ServerSettings());

        Assert.Empty(built.Services.GetServices<IValidateOptions<RaskDatabaseProviderCheck<RaskAppDbContext>>>());
    }

    private static void VerifyProvider<TContext>(IHost built)
        where TContext : DbContext =>
        _ = built.Services.GetRequiredService<IOptions<RaskDatabaseProviderCheck<TContext>>>().Value;

    private static IEnumerable<string> HostedServices(IHost built) =>
        built.Services.GetServices<IHostedService>().Select(s => s.GetType().Name);

    private static Dictionary<string, string?> ServerSettings() => Settings("postgres", PostgresApp);

    private static Dictionary<string, string?> Settings(string provider, string connectionString) => new()
    {
        ["Rask:Database:Provider"] = provider,
        ["Rask:ConnectionStrings:App"] = connectionString,
    };

    private static RaskApp CreateApp(Dictionary<string, string?>? settings) =>
        RaskApp.Create([], b =>
        {
            b.WebHost.UseSetting("urls", "http://127.0.0.1:0");
            if (settings is not null)
            {
                b.Configuration.AddInMemoryCollection(settings);
            }
        });

    private static WebApplication Build(Dictionary<string, string?>? settings, Action<RaskApp>? arrange = null)
    {
        var app = CreateApp(settings);
        arrange?.Invoke(app);
        return app.Build<TestApp>();
    }

    private static ServiceProvider Services(string? provider, string connectionString)
    {
        var values = new Dictionary<string, string?> { ["Rask:ConnectionStrings:App"] = connectionString };
        if (provider is not null)
        {
            values["Rask:Database:Provider"] = provider;
        }

        return new ServiceCollection()
            .AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(values).Build())
            .BuildServiceProvider();
    }

    private static string Messages(Exception error)
    {
        var messages = new List<string>();
        for (Exception? e = error; e is not null; e = e.InnerException)
        {
            messages.Add(e.Message);
        }

        return string.Join(" | ", messages);
    }
}
