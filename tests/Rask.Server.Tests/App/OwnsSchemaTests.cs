using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Rask.Server.Tests.App;

/// <summary>
///     <c>AddRaskData&lt;TContext&gt;(…).OwnsSchema(…)</c>: a hand-wired host applies its migrations as it starts, and
///     takes over a database whose schema predates them.
/// </summary>
/// <remarks>
///     Against a real SQLite file and two hand-written migrations: <see cref="LedgerBaseline"/> makes the table an app
///     moving onto Rask already has, <see cref="LedgerMemo"/> is the first real migration after it.
/// </remarks>
public sealed class OwnsSchemaTests : IDisposable
{
    private readonly LedgerDatabase _db = new();

    [Fact]
    public async Task A_fresh_database_is_made_by_the_migrations_from_the_first()
    {
        using var host = Host(s => s.WhenTable("Ledgers"));

        await host.StartAsync(TestContext.Current.CancellationToken);
        await host.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal([LedgerBaseline.Id, LedgerMemo.Id], await _db.History());
    }

    [Fact]
    public async Task An_existing_schema_without_history_is_taken_over_and_the_later_migrations_apply()
    {
        await _db.Run("CREATE TABLE \"Ledgers\" (\"Id\" INTEGER NOT NULL CONSTRAINT \"PK_Ledgers\" PRIMARY KEY AUTOINCREMENT, \"Title\" TEXT NOT NULL); "
                  + "INSERT INTO \"Ledgers\" (\"Title\") VALUES ('kept');");
        using var host = Host(s => s.WhenTable("Ledgers"));

        await host.StartAsync(TestContext.Current.CancellationToken);
        await host.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal([LedgerBaseline.Id, LedgerMemo.Id], await _db.History());
        Assert.Equal("kept|", await _db.Scalar("SELECT \"Title\" || '|' || COALESCE(\"Memo\", '') FROM \"Ledgers\""));
    }

    [Fact]
    public async Task A_schema_taken_over_is_left_as_it_is_by_the_next_start()
    {
        await _db.Run("CREATE TABLE \"Ledgers\" (\"Id\" INTEGER NOT NULL CONSTRAINT \"PK_Ledgers\" PRIMARY KEY AUTOINCREMENT, \"Title\" TEXT NOT NULL); "
                  + "INSERT INTO \"Ledgers\" (\"Title\") VALUES ('kept');");
        await StartAndStop();
        var objectsAfterTheTakeover = await Objects();
        var historyAfterTheTakeover = await _db.History();

        await StartAndStop();

        Assert.Equal(objectsAfterTheTakeover, await Objects());
        Assert.Equal(historyAfterTheTakeover, await _db.History());
        Assert.Equal("kept|", await _db.Scalar("SELECT \"Title\" || '|' || COALESCE(\"Memo\", '') FROM \"Ledgers\""));
    }

    [Fact]
    public async Task Two_hosts_starting_together_take_the_schema_over_once()
    {
        await _db.Run("CREATE TABLE \"Ledgers\" (\"Id\" INTEGER NOT NULL CONSTRAINT \"PK_Ledgers\" PRIMARY KEY AUTOINCREMENT, \"Title\" TEXT NOT NULL);");
        using var blue = Host(s => s.WhenTable("Ledgers"));
        using var green = Host(s => s.WhenTable("Ledgers"));

        await Task.WhenAll(
            blue.StartAsync(TestContext.Current.CancellationToken),
            green.StartAsync(TestContext.Current.CancellationToken));
        await Task.WhenAll(
            blue.StopAsync(TestContext.Current.CancellationToken),
            green.StopAsync(TestContext.Current.CancellationToken));

        Assert.Equal([LedgerBaseline.Id, LedgerMemo.Id], await _db.History());
    }

    [Fact]
    public async Task A_database_the_old_migrations_made_is_taken_over_by_the_name_of_their_last()
    {
        await _db.Run("CREATE TABLE \"Ledgers\" (\"Id\" INTEGER NOT NULL CONSTRAINT \"PK_Ledgers\" PRIMARY KEY AUTOINCREMENT, \"Title\" TEXT NOT NULL); "
                  + "CREATE TABLE \"__EFMigrationsHistory\" (\"MigrationId\" TEXT NOT NULL CONSTRAINT \"PK___EFMigrationsHistory\" PRIMARY KEY, \"ProductVersion\" TEXT NOT NULL); "
                  + "INSERT INTO \"__EFMigrationsHistory\" VALUES ('20250101000000_OldLedgers', '9.0.0');");
        using var host = Host(s => s.TakesOverFrom("OldLedgers"));

        await host.StartAsync(TestContext.Current.CancellationToken);
        await host.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["20250101000000_OldLedgers", LedgerBaseline.Id, LedgerMemo.Id], await _db.History());
    }

    [Fact]
    public async Task A_table_without_the_old_migration_in_its_history_stops_the_start()
    {
        await _db.Run("CREATE TABLE \"Ledgers\" (\"Id\" INTEGER NOT NULL CONSTRAINT \"PK_Ledgers\" PRIMARY KEY AUTOINCREMENT, \"Title\" TEXT NOT NULL);");
        using var host = Host(s => s.TakesOverFrom("OldLedgers").WhenTable("Ledgers"));

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => host.StartAsync(TestContext.Current.CancellationToken));

        Assert.Contains("does not show OldLedgers applied", refused.Message, StringComparison.Ordinal);
        Assert.Empty(await _db.History());
    }

    [Fact]
    public async Task Patience_is_the_command_timeout_each_migration_runs_with()
    {
        var witness = new MemoTimeoutWitness();
        using var host = Host(s => s.Patience(20.Minutes), witness);

        await host.StartAsync(TestContext.Current.CancellationToken);
        await host.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal((int)20.Minutes.TotalSeconds, witness.AddingTheMemo);
    }

    [Fact]
    public async Task Without_OwnsSchema_a_hand_wired_host_leaves_the_database_alone()
    {
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.Services.AddRaskData<LedgerContext>(o => o.UseSqlite(_db.Connection));
        using var host = builder.Build();

        await host.StartAsync(TestContext.Current.CancellationToken);
        await host.StopAsync(TestContext.Current.CancellationToken);

        Assert.True(await _db.IsEmpty(), "nothing should have created a table in the database");
    }

    public void Dispose() => _db.Dispose();

    private async Task StartAndStop()
    {
        using var host = Host(s => s.WhenTable("Ledgers"));
        await host.StartAsync(TestContext.Current.CancellationToken);
        await host.StopAsync(TestContext.Current.CancellationToken);
    }

    // Every table, index and trigger in the file, by kind and name: what a second takeover must not add to.
    private Task<object?> Objects() =>
        _db.Scalar("SELECT group_concat(\"type\" || ':' || \"name\", ',') FROM (SELECT \"type\", \"name\" FROM sqlite_master ORDER BY \"type\", \"name\")");

    private IHost Host(Action<SchemaOwnership> ownership, MemoTimeoutWitness? witness = null)
    {
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.Services
            .AddRaskData<LedgerContext>(o =>
            {
                o.UseSqlite(_db.Connection);
                if (witness is not null)
                {
                    o.AddInterceptors(witness);
                }
            })
            .OwnsSchema(ownership);
        return builder.Build();
    }
}

/// <summary>A row of the table the app had before its migrations.</summary>
public sealed class Shelf
{
    public int Id { get; set; }

    public string Title { get; set; } = "";

    public string? Memo { get; set; }
}

/// <summary>A context whose first migration makes a schema that already exists in the databases it takes over.</summary>
public sealed class LedgerContext(DbContextOptions<LedgerContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<Shelf>().ToTable("Ledgers");
}

[DbContext(typeof(LedgerContext))]
[Migration(Id)]
public sealed class LedgerBaseline : Migration
{
    public const string Id = "20261101000000_Baseline";

    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.CreateTable(
            name: "Ledgers",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false).Annotation("Sqlite:Autoincrement", true),
                Title = table.Column<string>(type: "TEXT", nullable: false),
            },
            constraints: table => table.PrimaryKey("PK_Ledgers", x => x.Id));
}

[DbContext(typeof(LedgerContext))]
[Migration(Id)]
public sealed class LedgerMemo : Migration
{
    public const string Id = "20261102000000_Memo";

    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AddColumn<string>(name: "Memo", table: "Ledgers", type: "TEXT", nullable: true);
}

[DbContext(typeof(LedgerContext))]
public sealed class LedgerContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder)
    {
        modelBuilder.HasAnnotation("ProductVersion", "10.0.13");
        modelBuilder.Entity("Rask.Server.Tests.App.Shelf", b =>
        {
            b.Property<int>("Id").ValueGeneratedOnAdd().HasColumnType("INTEGER");
            b.Property<string>("Memo").HasColumnType("TEXT");
            b.Property<string>("Title").IsRequired().HasColumnType("TEXT");
            b.HasKey("Id");
            b.ToTable("Ledgers");
        });
    }
}
