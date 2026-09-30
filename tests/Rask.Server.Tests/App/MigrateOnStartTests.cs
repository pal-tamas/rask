using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Rask.Server.Tests.App;

/// <summary>
///     A <see cref="RaskApp"/> applies its pending migrations when it starts, before anything that needs the tables.
/// </summary>
/// <remarks>
///     Against a real SQLite file and real, hand-written migrations: the app's own context (<see cref="AccountContext"/>)
///     goes through exactly the wiring <see cref="RaskAppDbContext"/> does, and a separate test shows Rask's own context
///     reaches it too. Every other battery is off, so the only thing touching the database is the migration.
/// </remarks>
[Collection(RaskAppCollection.Name)]
public sealed class MigrateOnStartTests
{
    [Fact]
    public async Task An_app_with_a_pending_migration_starts_with_it_applied()
    {
        using var database = new TempDatabase();
        var app = DataOnlyApp<AccountContext>(database);

        await app.StartAsync(TestContext.Current.CancellationToken);
        await app.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal([CreateAccounts.Id], await Applied<AccountContext>(app));
    }

    [Fact]
    public async Task Turning_MigrateOnStart_off_in_code_leaves_the_migration_unapplied()
    {
        using var database = new TempDatabase();
        var app = DataOnlyApp<AccountContext>(database, arrange: a => a.Configure(c => c.MigrateOnStart = false));

        await app.StartAsync(TestContext.Current.CancellationToken);
        await app.StopAsync(TestContext.Current.CancellationToken);

        Assert.False(File.Exists(database.Path), "nothing should have opened the database");
    }

    [Fact]
    public async Task The_setting_turns_migrate_on_start_off_as_well()
    {
        using var database = new TempDatabase();
        var app = DataOnlyApp<AccountContext>(database, setting: "false");

        await app.StartAsync(TestContext.Current.CancellationToken);
        await app.StopAsync(TestContext.Current.CancellationToken);

        Assert.False(File.Exists(database.Path), "nothing should have opened the database");
    }

    [Fact]
    public async Task Code_wins_over_the_setting_when_both_are_given()
    {
        using var database = new TempDatabase();
        var app = DataOnlyApp<AccountContext>(
            database, setting: "false", arrange: a => a.Configure(c => c.MigrateOnStart = true));

        await app.StartAsync(TestContext.Current.CancellationToken);
        await app.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal([CreateAccounts.Id], await Applied<AccountContext>(app));
    }

    [Fact]
    public async Task A_model_changed_since_its_last_migration_fails_the_start_and_names_the_fix()
    {
        using var database = new TempDatabase();
        var app = DataOnlyApp<DriftedAccountContext>(database);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => app.StartAsync(TestContext.Current.CancellationToken));

        Assert.Contains("has changed since its last migration", error.Message, StringComparison.Ordinal);
        Assert.Contains("rask db add <Name>", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_app_with_no_migrations_starts_and_says_how_to_add_the_first()
    {
        using var database = new TempDatabase();
        var logs = new CapturingLoggerProvider();
        var app = RaskApp.Create([], b =>
        {
            b.WebHost.UseSetting("urls", "http://127.0.0.1:0");
            b.Logging.Services.AddSingleton<ILoggerProvider>(logs);
        });
        app.Configure(c =>
        {
            c.ConnectionString = $"Data Source={database.Path}";
            BatteriesOff(c);
        });
        var built = app.Build<MinimalApp>();

        await built.StartAsync(TestContext.Current.CancellationToken);
        await built.StopAsync(TestContext.Current.CancellationToken);

        Assert.Contains(logs.Messages, m => m.Contains("RaskAppDbContext has no migrations yet", StringComparison.Ordinal)
                                            && m.Contains("rask db add Init", StringComparison.Ordinal));
        Assert.False(File.Exists(database.Path), "no schema should have been created without a migration");
    }

    [Fact]
    public async Task With_the_data_battery_off_no_migration_is_attempted()
    {
        using var database = new TempDatabase();
        var app = DataOnlyApp<AccountContext>(database, arrange: a => a.Configure(c => c.Data.Off()));

        await app.StartAsync(TestContext.Current.CancellationToken);
        await app.StopAsync(TestContext.Current.CancellationToken);

        Assert.False(File.Exists(database.Path), "nothing should have opened the database");
    }

    private static WebApplication DataOnlyApp<TContext>(
        TempDatabase database, string? setting = null, Action<RaskApp>? arrange = null)
        where TContext : DbContext
    {
        var app = RaskApp.Create([], b =>
        {
            b.WebHost.UseSetting("urls", "http://127.0.0.1:0");
            if (setting is not null)
            {
                b.Configuration["Rask:Database:MigrateOnStart"] = setting;
            }
        });
        app.Configure(BatteriesOff);
        app.Services.AddDbContextFactory<TContext>(o => o.UseSqlite($"Data Source={database.Path};Pooling=False"));
        arrange?.Invoke(app);
        return app.Build<MinimalApp>();
    }

    private static void BatteriesOff(RaskAppOptions c)
    {
        c.Auth.Off();
        c.Jobs.Off();
        c.Mail.Off();
        c.Cache.Off();
        c.Storage.Off();
        c.Push.Off();
        c.Ops.Off();
        c.Snapshots.Off();
    }

    private static async Task<List<string>> Applied<TContext>(WebApplication app)
        where TContext : DbContext
    {
        await using var db = await app.Services.GetRequiredService<IDbContextFactory<TContext>>().CreateDbContextAsync();
        return [.. await db.Database.GetAppliedMigrationsAsync()];
    }

    private sealed class TempDatabase : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"rask-migrate-{Guid.NewGuid():N}.db");

        public void Dispose()
        {
            foreach (var file in new[] { Path, Path + "-wal", Path + "-shm" })
            {
                File.Delete(file);
            }
        }
    }
}

/// <summary>A row the migrations below create a table for.</summary>
public sealed class Account
{
    public int Id { get; set; }

    public string Title { get; set; } = "";
}

/// <summary>An app-owned context whose one migration matches its model.</summary>
public sealed class AccountContext(DbContextOptions<AccountContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<Account>().ToTable("Accounts");
}

/// <summary>The same migration, but the model has since gained an index nobody migrated.</summary>
public sealed class DriftedAccountContext(DbContextOptions<DriftedAccountContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<Account>(b =>
        {
            b.ToTable("Accounts");
            b.HasIndex(l => l.Title);
        });
}

[DbContext(typeof(AccountContext))]
[Migration(Id)]
public sealed class CreateAccounts : Migration
{
    public const string Id = "20260928000000_CreateAccounts";

    protected override void Up(MigrationBuilder migrationBuilder) => AccountSchema.Create(migrationBuilder);
}

[DbContext(typeof(AccountContext))]
public sealed class AccountContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder) => AccountSchema.Snapshot(modelBuilder);
}

[DbContext(typeof(DriftedAccountContext))]
[Migration("20260928000000_CreateAccounts")]
public sealed class CreateDriftedAccounts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => AccountSchema.Create(migrationBuilder);
}

[DbContext(typeof(DriftedAccountContext))]
public sealed class DriftedAccountContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder) => AccountSchema.Snapshot(modelBuilder);
}

/// <summary>What <c>dotnet ef migrations add</c> writes for <see cref="Account"/>, shared by both contexts.</summary>
internal static class AccountSchema
{
    public static void Create(MigrationBuilder migrationBuilder) =>
        migrationBuilder.CreateTable(
            name: "Accounts",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false).Annotation("Sqlite:Autoincrement", true),
                Title = table.Column<string>(type: "TEXT", nullable: false),
            },
            constraints: table => table.PrimaryKey("PK_Accounts", x => x.Id));

    public static void Snapshot(ModelBuilder modelBuilder)
    {
        modelBuilder.HasAnnotation("ProductVersion", "10.0.12");
        modelBuilder.Entity("Rask.Server.Tests.App.Account", b =>
        {
            b.Property<int>("Id").ValueGeneratedOnAdd().HasColumnType("INTEGER");
            b.Property<string>("Title").IsRequired().HasColumnType("TEXT");
            b.HasKey("Id");
            b.ToTable("Accounts");
        });
    }
}
