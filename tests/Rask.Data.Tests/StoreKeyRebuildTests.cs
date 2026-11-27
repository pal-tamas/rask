using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Rask.Data.Tests;

/// <summary>A queue row as a battery stored it before its key was the app's: numbered by the store.</summary>
public sealed class NumberedTicket
{
    public long Id { get; set; }

    public string Subject { get; set; } = "";

    public Guid? UserId { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? ProcessedAt { get; set; }
}

/// <summary>The same row today: a Guid key Rask makes before the insert.</summary>
public sealed class GuidTicket
{
    public Guid Id { get; set; }

    public string Subject { get; set; } = "";

    public Guid? UserId { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? ProcessedAt { get; set; }
}

/// <summary>The table before: an identity key.</summary>
public sealed class NumberedTicketContext(DbContextOptions<NumberedTicketContext> options) : DbContext(options)
{
    public DbSet<NumberedTicket> Tickets => Set<NumberedTicket>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<NumberedTicket>(b =>
        {
            b.ToTable("Tickets");
            b.HasIndex(x => new { x.ProcessedAt, x.Id });
        });
}

/// <summary>The table after, marked for the rebuild: <paramref name="keepBy" /> null drops the rows.</summary>
public sealed class GuidTicketContext(DbContextOptions<GuidTicketContext> options, string? keepBy, bool marked = true) : DbContext(options)
{
    public DbSet<GuidTicket> Tickets => Set<GuidTicket>();

    internal (string? KeepBy, bool Marked) Shape => (keepBy, marked);

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder
            .ReplaceService<IMigrationsModelDiffer, RaskMigrationsModelDiffer>()
            .ReplaceService<IModelCacheKeyFactory, GuidTicketModelKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<GuidTicket>(b =>
        {
            b.ToTable("Tickets");
            b.Property(x => x.Id).ValueGeneratedNever();
            b.HasIndex(x => new { x.ProcessedAt, x.Id });
            if (marked)
            {
                b.RebuildsStoreKey(nameof(GuidTicket.ProcessedAt), keepBy);
            }
        });
}

internal sealed class GuidTicketModelKey : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime) => (context.GetType(), ((GuidTicketContext)context).Shape, designTime);
}

/// <summary>
///     A battery table whose key turns from a store-numbered integer into a client-made Guid is rebuilt by its migration:
///     refused while a row waits, its processed rows kept under ids made from their time, or dropped as history.
/// </summary>
public sealed class StoreKeyRebuildTests : IDisposable
{
    private static readonly DateTime Monday = new(2026, 11, 23, 8, 30, 15, 250, DateTimeKind.Utc);

    private static readonly Guid Owner = Guid.Parse("3e18ad13-d95e-4808-97bd-918b457c006b");

    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    public StoreKeyRebuildTests() => _connection.Open();

    public void Dispose() => _connection.Dispose();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public void The_key_change_is_a_rebuild_opened_by_the_exclusive_marker_not_an_altered_column()
    {
        using var before = Numbered();
        using var after = Guids(nameof(GuidTicket.CreatedAt));

        var operations = Differences(before, after);

        var guard = Assert.IsType<SqlOperation>(operations[0]).Sql;
        Assert.StartsWith(ExclusiveMigrations.Prefix + ": rebuilds the key of \"Tickets\" as a client-made Guid", guard, StringComparison.Ordinal);
        Assert.Contains("\"ProcessedAt\" IS NULL", guard, StringComparison.Ordinal);
        Assert.IsType<DropTableOperation>(operations[1]);
        Assert.IsType<CreateTableOperation>(operations[2]);
        Assert.Contains(operations, static o => o is CreateIndexOperation);
        Assert.StartsWith("INSERT INTO \"Tickets\"", Assert.IsType<SqlOperation>(operations[^1]).Sql, StringComparison.Ordinal);
        Assert.DoesNotContain(operations, static o => o is AlterColumnOperation);
        Assert.True(ExclusiveMigrations.IsExclusive(new Written(operations)));
    }

    [Fact]
    public void A_table_the_model_does_not_mark_is_left_to_EF_Core()
    {
        using var before = Numbered();
        using var unmarked = new GuidTicketContext(new DbContextOptionsBuilder<GuidTicketContext>().UseSqlite(_connection).Options, null, marked: false);
        using var after = Guids(nameof(GuidTicket.CreatedAt));

        var altered = Differences(before, unmarked);
        var again = Differences(after, after);

        Assert.Contains(altered, static o => o is AlterColumnOperation);
        Assert.DoesNotContain(altered, static o => o is SqlOperation);
        Assert.Empty(again);
    }

    [Fact]
    public async Task A_waiting_row_refuses_the_migration_and_leaves_the_table_as_it_was()
    {
        using var before = Numbered();
        using var after = Guids(nameof(GuidTicket.CreatedAt));
        await SeedAsync(before, waiting: true);

        var refused = await Assert.ThrowsAsync<SqliteException>(() => MigrateAsync(before, after));

        Assert.Contains("rows with no ProcessedAt are still waiting", refused.Message, StringComparison.Ordinal);
        Assert.Equal(2, await before.Tickets.CountAsync(Cancellation));
    }

    [Fact]
    public async Task Processed_rows_are_kept_under_v7_ids_made_from_their_time_in_that_order()
    {
        using var before = Numbered();
        using var after = Guids(nameof(GuidTicket.CreatedAt));
        await SeedAsync(before, waiting: false);

        await MigrateAsync(before, after);

        var rows = await after.Tickets.OrderBy(t => t.Id).ToListAsync(Cancellation);
        Assert.Equal(["earlier", "later", "later still"], rows.Select(r => r.Subject));
        Assert.All(rows, r => Assert.Equal(7, r.Id.Version));
        Assert.Equal(Monday.AddMinutes(-5), rows[0].CreatedAt);
        Assert.Equal(Expected(Monday.AddMinutes(-5), 2), rows[0].Id);
        Assert.Equal(Expected(Monday, 3), rows[2].Id);
        Assert.Equal(Owner, rows[0].UserId);
    }

    [Fact]
    public async Task Rows_that_are_history_are_dropped_with_the_table()
    {
        using var before = Numbered();
        using var after = Guids(keepBy: null);
        await SeedAsync(before, waiting: false);

        await MigrateAsync(before, after);

        Assert.Equal(0, await after.Tickets.CountAsync(Cancellation));
        after.Tickets.Add(new GuidTicket { Id = Guid.CreateVersion7(), Subject = "new", CreatedAt = Monday });
        await after.SaveChangesAsync(Cancellation);
    }

    // The id a kept row gets: its millisecond, version 7, then its old number.
    private static Guid Expected(DateTime at, long number)
    {
        var ms = new DateTimeOffset(at).ToUnixTimeMilliseconds().ToString("x12", System.Globalization.CultureInfo.InvariantCulture);
        var old = number.ToString("x15", System.Globalization.CultureInfo.InvariantCulture);
        return Guid.Parse($"{ms[..8]}-{ms[8..]}-7000-8{old[..3]}-{old[3..]}");
    }

    private static async Task SeedAsync(NumberedTicketContext before, bool waiting)
    {
        await before.Database.EnsureCreatedAsync(Cancellation);

        // Numbered out of time order: the kept ids sort by the time first, the old number second.
        before.Tickets.Add(new NumberedTicket { Id = 1, Subject = "later", CreatedAt = Monday, ProcessedAt = Monday });
        if (waiting)
        {
            before.Tickets.Add(new NumberedTicket { Id = 9, Subject = "waiting", CreatedAt = Monday });
        }
        else
        {
            before.Tickets.Add(new NumberedTicket { Id = 2, Subject = "earlier", CreatedAt = Monday.AddMinutes(-5), ProcessedAt = Monday, UserId = Owner });
            before.Tickets.Add(new NumberedTicket { Id = 3, Subject = "later still", CreatedAt = Monday, ProcessedAt = Monday });
        }

        await before.SaveChangesAsync(Cancellation);
    }

    private async Task MigrateAsync(NumberedTicketContext before, GuidTicketContext after)
    {
        var commands = after.GetService<IMigrationsSqlGenerator>()
            .Generate(Differences(before, after), after.GetService<IDesignTimeModel>().Model);
        await after.GetService<IMigrationCommandExecutor>()
            .ExecuteNonQueryAsync(commands, after.GetService<Microsoft.EntityFrameworkCore.Storage.IRelationalConnection>(), Cancellation);
    }

    private static IReadOnlyList<MigrationOperation> Differences(DbContext before, GuidTicketContext after) =>
        after.GetService<IMigrationsModelDiffer>().GetDifferences(Model(before), Model(after));

    private static IRelationalModel Model(DbContext context) => context.GetService<IDesignTimeModel>().Model.GetRelationalModel();

    private NumberedTicketContext Numbered() =>
        new(new DbContextOptionsBuilder<NumberedTicketContext>().UseSqlite(_connection).Options);

    private GuidTicketContext Guids(string? keepBy) =>
        new(new DbContextOptionsBuilder<GuidTicketContext>().UseSqlite(_connection).Options, keepBy);

    private sealed class Written(IReadOnlyList<MigrationOperation> operations) : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Operations.AddRange(operations);
    }
}
