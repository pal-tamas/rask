using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Rask.Server;

namespace Rask.Providers.E2E.Tests;

/// <summary>
/// A hand-wired host whose migrations own the schema, started against a server that does not have its database yet:
/// the start creates it and applies the migrations, as EF Core's <c>Migrate</c> always did on its own.
/// </summary>
/// <remarks>
/// The regression it holds down: migrate-on-start opened its connection (to lift the session timeouts) BEFORE
/// <c>Migrate</c>, so a fresh app on an empty SQL Server or PostgreSQL failed to log in to the database it was about
/// to create. SQLite makes its file on open, and <c>MigrateOnStartTests</c> starts from no file.
/// </remarks>
internal static class FreshServerScenario
{
    /// <summary>Starts <paramref name="hosts"/> hosts together on <paramref name="provider"/>'s database, then reads the history.</summary>
    internal static async Task<List<string>> StartAsync(int hosts, Action<DbContextOptionsBuilder> provider)
    {
        var started = Enumerable.Range(0, hosts).Select(_ => Host(provider)).ToList();
        try
        {
            await Task.WhenAll(started.Select(h => h.StartAsync(TestContext.Current.CancellationToken)));
            await Task.WhenAll(started.Select(h => h.StopAsync(TestContext.Current.CancellationToken)));

            await using var db = await started[0].Services.GetRequiredService<IDbContextFactory<FreshNoteContext>>()
                .CreateDbContextAsync(TestContext.Current.CancellationToken);
            return [.. await db.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken)];
        }
        finally
        {
            foreach (var host in started)
            {
                host.Dispose();
            }
        }
    }

    /// <summary>Drops the scenario's database, which is always one of ours by name.</summary>
    internal static async Task DropAsync(Action<DbContextOptionsBuilder> provider)
    {
        var options = new DbContextOptionsBuilder<FreshNoteContext>();
        provider(options);
        await using var db = new FreshNoteContext(options.Options);
        await db.Database.EnsureDeletedAsync();
    }

    internal static string DatabaseName() => $"rask_fresh_{Guid.NewGuid():N}";

    private static IHost Host(Action<DbContextOptionsBuilder> provider)
    {
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.Services.AddRaskData<FreshNoteContext>(provider).OwnsSchema();
        return builder.Build();
    }
}

[Collection(PostgresCollection.Name)]
public sealed class PostgresMigrateOnStartFreshServerTests : IAsyncDisposable
{
    private readonly string _connection = Postgres.Available
        ? new NpgsqlConnectionStringBuilder(Postgres.Required) { Database = FreshServerScenario.DatabaseName() }.ConnectionString
        : "";

    [Fact]
    public async Task A_host_on_a_server_without_its_database_creates_it_and_applies_the_migrations()
    {
        Assert.SkipUnless(Postgres.Available, Postgres.SkipReason);

        var applied = await FreshServerScenario.StartAsync(1, Provider);

        Assert.Equal([CreateFreshNotes.Id], applied);
    }

    [Fact]
    public async Task Two_hosts_starting_together_on_a_server_without_their_database_both_start()
    {
        Assert.SkipUnless(Postgres.Available, Postgres.SkipReason);

        var applied = await FreshServerScenario.StartAsync(2, Provider);

        Assert.Equal([CreateFreshNotes.Id], applied);
    }

    public async ValueTask DisposeAsync()
    {
        if (Postgres.Available)
        {
            await FreshServerScenario.DropAsync(Provider);
        }
    }

    private void Provider(DbContextOptionsBuilder o) => o.UseNpgsql(_connection);
}

[Collection(SqlServerCollection.Name)]
public sealed class SqlServerMigrateOnStartFreshServerTests : IAsyncDisposable
{
    private readonly string _connection = SqlServer.Available
        ? SqlServer.Database(FreshServerScenario.DatabaseName())
        : "";

    [Fact]
    public async Task A_host_on_a_server_without_its_database_creates_it_and_applies_the_migrations()
    {
        Assert.SkipUnless(SqlServer.Available, SqlServer.SkipReason);

        var applied = await FreshServerScenario.StartAsync(1, Provider);

        Assert.Equal([CreateFreshNotes.Id], applied);
    }

    [Fact]
    public async Task Two_hosts_starting_together_on_a_server_without_their_database_both_start()
    {
        Assert.SkipUnless(SqlServer.Available, SqlServer.SkipReason);

        var applied = await FreshServerScenario.StartAsync(2, Provider);

        Assert.Equal([CreateFreshNotes.Id], applied);
    }

    public async ValueTask DisposeAsync()
    {
        if (SqlServer.Available)
        {
            await FreshServerScenario.DropAsync(Provider);
        }
    }

    private void Provider(DbContextOptionsBuilder o) => o.UseSqlServer(_connection);
}

/// <summary>A row of the one table the fresh database gets.</summary>
public sealed class FreshNote
{
    public Guid Id { get; set; }

    public string Title { get; set; } = "";
}

/// <summary>A context whose one migration makes its table, written for any provider.</summary>
public sealed class FreshNoteContext(DbContextOptions<FreshNoteContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<FreshNote>().ToTable("FreshNotes");
}

[DbContext(typeof(FreshNoteContext))]
[Migration(Id)]
public sealed class CreateFreshNotes : Migration
{
    public const string Id = "20261123000000_CreateFreshNotes";

    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.CreateTable(
            name: "FreshNotes",
            columns: table => new
            {
                Id = table.Column<Guid>(nullable: false),
                Title = table.Column<string>(nullable: false),
            },
            constraints: table => table.PrimaryKey("PK_FreshNotes", x => x.Id));
}

[DbContext(typeof(FreshNoteContext))]
public sealed class FreshNoteContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder)
    {
        modelBuilder.HasAnnotation("ProductVersion", "10.0.13");
        modelBuilder.Entity("Rask.Providers.E2E.Tests.FreshNote", b =>
        {
            b.Property<Guid>("Id").ValueGeneratedOnAdd();
            b.Property<string>("Title").IsRequired();
            b.HasKey("Id");
            b.ToTable("FreshNotes");
        });
    }
}
