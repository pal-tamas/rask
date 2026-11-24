using Microsoft.EntityFrameworkCore;

namespace Rask.Providers.E2E.Tests;

/// <summary>A ticket's number, a one-value value object stored in a column of its own; 0 is a ticket not numbered yet.</summary>
public readonly record struct TicketNumber(int Value)
{
    public static readonly TicketNumber Unset = new(0);
}

/// <summary>A ticket of a lane: numbered once issued, its copies pointing at the ticket they copy.</summary>
public sealed class LaneTicket
{
    public int Id { get; set; }

    public int Lane { get; set; }

    public TicketNumber Number { get; set; }

    public int? ParentId { get; set; }

    public int Desk { get; set; }
}

public sealed class OneValueFilterDbContext(DbContextOptions<OneValueFilterDbContext> options) : DbContext(options)
{
    public const string Schema = "rask_one_value_filter";

    public DbSet<LaneTicket> Tickets => Set<LaneTicket>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        ModelRegistry.ApplyConventions(configurationBuilder);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Database.IsSqlite() ? null : Schema);
        modelBuilder.Entity<LaneTicket>(b =>
        {
            ValueObjectColumn.Map(b.Property(t => t.Number), (TicketNumber v) => v.Value);
            b.HasIndex(t => new { t.Lane, t.Number }).IsUnique().Where(t => t.ParentId == null && t.Number.Value > 0);
            b.HasIndex(t => new { t.Desk, t.Number }).IsUnique().Where(t => t.Number != TicketNumber.Unset);
        });
    }
}

/// <summary>
///     A filtered index over a one-value value object holds on every provider as the C# says: its <c>.Value</c> compared
///     with a number, and the object compared with one of its own.
/// </summary>
public abstract class OneValueFilterParityTests(ProviderLeg leg) : ParityTests<OneValueFilterDbContext>(leg)
{
    [Fact]
    public async Task A_filter_on_the_held_value_keeps_the_unnumbered_and_the_copies_out()
    {
        RequireServer();
        await using var db = Open();
        db.Tickets.AddRange(
            Ticket(lane: 1, number: 0, desk: 1),
            Ticket(lane: 1, number: 0, desk: 2),
            Ticket(lane: 1, number: 5, desk: 3),
            Ticket(lane: 1, number: 5, desk: 4, parent: 1));
        await db.SaveChangesAsync(Ct);

        db.Tickets.Add(Ticket(lane: 1, number: 5, desk: 5));

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));
    }

    [Fact]
    public async Task A_filter_against_a_value_object_constant_keeps_that_value_out()
    {
        RequireServer();
        await using var db = Open();
        db.Tickets.AddRange(
            Ticket(lane: 1, number: 0, desk: 7),
            Ticket(lane: 2, number: 0, desk: 7),
            Ticket(lane: 3, number: 9, desk: 7));
        await db.SaveChangesAsync(Ct);

        db.Tickets.Add(Ticket(lane: 4, number: 9, desk: 7));

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));
    }

    private protected override OneValueFilterDbContext Open() => Leg.Open<OneValueFilterDbContext>(options => new(options));

    private static LaneTicket Ticket(int lane, int number, int desk, int? parent = null) =>
        new() { Lane = lane, Number = new TicketNumber(number), Desk = desk, ParentId = parent };
}

public sealed class SqliteOneValueFilterParityTests() : OneValueFilterParityTests(ProviderLeg.Sqlite("one-value-filter"));

[Collection(SqlServerCollection.Name)]
public sealed class SqlServerOneValueFilterParityTests() : OneValueFilterParityTests(ProviderLeg.SqlServer("rask_e2e_one_value_filter"));

[Collection(PostgresCollection.Name)]
public sealed class PostgresOneValueFilterParityTests() : OneValueFilterParityTests(ProviderLeg.Postgres(OneValueFilterDbContext.Schema));
