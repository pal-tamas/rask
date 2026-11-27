using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Rask.Data;
using Rask.Tests.Shared;

namespace Rask.Server.Tests.App;

/// <summary>
///     <c>dotnet App.dll rask …</c>: a <see cref="RaskApp" /> started with a command runs it against its database through
///     its built, never started host — <c>rask db migrate</c> applies the migrations and their data steps.
/// </summary>
[Collection(RaskAppCollection.Name)]
public sealed class AppCommandTests : IDisposable
{
    private readonly InMemorySqlite _database = new();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => _database.Dispose();

    [Fact]
    public void Only_arguments_that_start_with_rask_name_a_command()
    {
        string[] serve = ["--urls", "http://localhost:5000"];

        var named = AppCommand.Names(["rask", "db", "migrate"]);

        Assert.True(named);
        Assert.False(AppCommand.Names(serve));
        Assert.False(AppCommand.Names([]));
    }

    [Fact]
    public async Task An_unknown_command_says_what_there_is_and_exits_two()
    {
        await using var services = new ServiceCollection().BuildServiceProvider();
        using var error = new StringWriter();

        var exit = await AppCommand.RunAsync(services, ["rask", "frobnicate"], error, Token);

        Assert.Equal(2, exit);
        Assert.Contains("Unknown command 'rask frobnicate'", error.ToString(), StringComparison.Ordinal);
        Assert.Contains("rask db migrate", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rask_db_migrate_applies_the_migrations_and_their_data_steps_with_MigrateOnStart_off()
    {
        var app = StepApp(migrateOnStart: false);
        using var log = AppStepLog.Begin();

        var exit = await AppCommand.RunAsync(app.Services, ["rask", "db", "migrate"], TextWriter.Null, Token);

        Assert.Equal(0, exit);
        Assert.Equal([CreateTallies.Id, FillTallies.Id], await AppliedAsync(app));
        Assert.Equal(["filled"], log.Steps);
    }

    [Fact]
    public async Task A_start_runs_the_data_steps_of_the_migrations_it_applies()
    {
        var app = StepApp(migrateOnStart: true);
        using var log = AppStepLog.Begin();

        await app.StartAsync(Token);
        await app.StopAsync(Token);

        Assert.Equal(["filled"], log.Steps);
    }

    [Fact]
    public async Task A_migration_that_fails_says_why_and_exits_one()
    {
        await using var services = new ServiceCollection().AddSingleton<IAppMigration, FailingMigration>().BuildServiceProvider();
        using var error = new StringWriter();

        var exit = await AppCommand.RunAsync(services, ["rask", "db", "migrate"], error, Token);

        Assert.Equal(1, exit);
        Assert.Contains("rask db migrate failed: the database is not there", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_command_that_needs_the_database_says_so_when_the_data_battery_is_off()
    {
        var app = RaskApp.Create([], b => b.WebHost.UseSetting("urls", "http://127.0.0.1:0"));
        app.Configure(c => c.Data.Off());
        var built = app.Build<MinimalApp>();
        using var error = new StringWriter();

        var exit = await AppCommand.RunAsync(built.Services, ["rask", "db", "migrate"], error, Token);

        Assert.Equal(2, exit);
        Assert.Contains("Data battery off", error.ToString(), StringComparison.Ordinal);
    }

    private WebApplication StepApp(bool migrateOnStart)
    {
        var app = RaskApp.Create([], b => b.WebHost.UseSetting("urls", "http://127.0.0.1:0"));
        app.Configure(c =>
        {
            c.MigrateOnStart = migrateOnStart;
            c.Auth.Off();
            c.Jobs.Off();
            c.Mail.Off();
            c.Cache.Off();
            c.Storage.Off();
            c.Push.Off();
            c.Ops.Off();
            c.Snapshots.Off();
        });
        app.Services.AddDbContextFactory<TallyContext>(o => o.UseSqlite(_database.ConnectionString));
        return app.Build<MinimalApp>();
    }

    private static async Task<List<string>> AppliedAsync(WebApplication app)
    {
        await using var db = await app.Services.GetRequiredService<IDbContextFactory<TallyContext>>().CreateDbContextAsync(Token);
        return [.. await db.Database.GetAppliedMigrationsAsync(Token)];
    }
}

/// <summary>A migration that cannot reach its database.</summary>
internal sealed class FailingMigration : IAppMigration
{
    public Task MigrateAsync(CancellationToken cancellationToken) =>
        throw new InvalidOperationException("the database is not there");
}

/// <summary>What the data step below did, in the flow of the test that ran it.</summary>
internal sealed class AppStepLog : IDisposable
{
    private static readonly AsyncLocal<AppStepLog?> Current = new();

    public List<string> Steps { get; } = [];

    public static AppStepLog Begin() => Current.Value = new AppStepLog();

    public static Task Note(string step)
    {
        Current.Value?.Steps.Add(step);
        return Task.CompletedTask;
    }

    public void Dispose() => Current.Value = null;
}

/// <summary>A row the migrations below create a table for.</summary>
public sealed class Tally
{
    public int Id { get; set; }

    public int Count { get; set; }
}

/// <summary>An app-owned context whose second migration is a data step.</summary>
public sealed class TallyContext(DbContextOptions<TallyContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.Entity<Tally>().ToTable("Tallies");
}

[DbContext(typeof(TallyContext))]
[Migration(Id)]
public sealed class CreateTallies : Migration
{
    public const string Id = "20261124000000_CreateTallies";

    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.CreateTable(
            name: "Tallies",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false).Annotation("Sqlite:Autoincrement", true),
                Count = table.Column<int>(type: "INTEGER", nullable: false),
            },
            constraints: table => table.PrimaryKey("PK_Tallies", x => x.Id));
}

[DbContext(typeof(TallyContext))]
[Migration(Id)]
public sealed class FillTallies : Migration
{
    public const string Id = "20261124000001_FillTallies";

    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Data(() => AppStepLog.Note("filled"));
}

[DbContext(typeof(TallyContext))]
public sealed class TallyContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder)
    {
        modelBuilder.HasAnnotation("ProductVersion", "11.0.0");
        modelBuilder.Entity("Rask.Server.Tests.App.Tally", b =>
        {
            b.Property<int>("Id").ValueGeneratedOnAdd().HasColumnType("INTEGER");
            b.Property<int>("Count").HasColumnType("INTEGER");
            b.HasKey("Id");
            b.ToTable("Tallies");
        });
    }
}
