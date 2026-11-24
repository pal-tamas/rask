using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Rask.Data;

namespace Rask.Server.Tests.App;

/// <summary>
///     <c>dotnet App.dll rask …</c>: a <see cref="RaskApp" /> started with a command runs it against its database through
///     its built, never started host — <c>rask db migrate</c> applies the migrations and their data steps.
/// </summary>
[Collection(RaskAppCollection.Name)]
public sealed class AppCommandTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"rask-command-{Guid.NewGuid():N}.db");

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        foreach (var file in new[] { _path, _path + "-wal", _path + "-shm" })
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void Only_arguments_that_start_with_rask_name_a_command()
    {
        string[] serve = ["--urls", "http://localhost:5000"];

        var named = AppCommand.Names(["rask", "audit"]);

        Assert.True(named);
        Assert.False(AppCommand.Names(serve));
        Assert.False(AppCommand.Names([]));
    }

    [Fact]
    public async Task An_unknown_command_says_what_there_is_and_exits_two()
    {
        await using var services = new ServiceCollection().BuildServiceProvider();
        using var error = new StringWriter();

        var exit = await AppCommand.RunAsync(services, ["rask", "frobnicate"], TextWriter.Null, error, Token);

        Assert.Equal(2, exit);
        Assert.Contains("Unknown command 'rask frobnicate'", error.ToString(), StringComparison.Ordinal);
        Assert.Contains("rask audit", error.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--aggregate")]
    [InlineData("--everything")]
    public async Task An_audit_option_it_does_not_take_exits_two_before_reading_anything(string option)
    {
        await using var services = new ServiceCollection().BuildServiceProvider();
        using var error = new StringWriter();

        var exit = await AppCommand.RunAsync(services, ["rask", "audit", option], TextWriter.Null, error, Token);

        Assert.Equal(2, exit);
        Assert.Contains("--aggregate <Name>", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_audit_takes_names_repeated_or_split_by_commas()
    {
        var options = AuditOptions.Parse(["--aggregate", "Driver,Vehicle", "-a", "Tank", "--json"]);

        Assert.Equal(["Driver", "Vehicle", "Tank"], options!.Aggregates);
        Assert.True(options.Json);
    }

    [Fact]
    public void The_report_names_each_aggregate_its_broken_rules_by_field_and_the_ids_alone()
    {
        var report = new RuleAuditReport(
            [new RuleAuditCount("Driver", 412), new RuleAuditCount("Vehicle", 3)],
            [new RuleBreach("Driver", "Chips[*].Period", "The end cannot be before the start.", ["7", "9"])]);

        var text = AuditOutput.Text(report);
        var json = AuditOutput.Json(report);

        Assert.Contains("2 aggregate(s), 415 row(s) read, 1 rule(s) broken.", text, StringComparison.Ordinal);
        Assert.Contains("  Chips[*].Period: The end cannot be before the start. — 2\n    7, 9", text.ReplaceLineEndings("\n"), StringComparison.Ordinal);
        Assert.Contains("Vehicle — 3 read, all pass.", text, StringComparison.Ordinal);
        Assert.Contains("\"count\": 2", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rask_db_migrate_applies_the_migrations_and_their_data_steps_with_MigrateOnStart_off()
    {
        var app = StepApp(migrateOnStart: false);
        using var log = AppStepLog.Begin();

        var exit = await AppCommand.RunAsync(app.Services, ["rask", "db", "migrate"], TextWriter.Null, TextWriter.Null, Token);

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
    public async Task A_command_that_needs_the_database_says_so_when_the_data_battery_is_off()
    {
        var app = RaskApp.Create([], b => b.WebHost.UseSetting("urls", "http://127.0.0.1:0"));
        app.Configure(c => c.Data.Off());
        var built = app.Build<MinimalApp>();
        using var error = new StringWriter();

        var exit = await AppCommand.RunAsync(built.Services, ["rask", "audit"], TextWriter.Null, error, Token);

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
        app.Services.AddDbContextFactory<TallyContext>(o => o.UseSqlite($"Data Source={_path};Pooling=False"));
        return app.Build<MinimalApp>();
    }

    private static async Task<List<string>> AppliedAsync(WebApplication app)
    {
        await using var db = await app.Services.GetRequiredService<IDbContextFactory<TallyContext>>().CreateDbContextAsync(Token);
        return [.. await db.Database.GetAppliedMigrationsAsync(Token)];
    }
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
