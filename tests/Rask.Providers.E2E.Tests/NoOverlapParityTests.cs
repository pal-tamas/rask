using Microsoft.EntityFrameworkCore;

namespace Rask.Providers.E2E.Tests;

/// <summary>When a chip is held: from a day, to a day before which it is given back, or open.</summary>
public sealed record HoldPeriod(DateOnly From, DateOnly? To);

/// <summary>A chip held by a driver for a while — TA's rule: a chip is one driver's at a time.</summary>
public sealed class ChipHold
{
    public int Id { get; set; }

    public string Tenant { get; set; } = "";

    public string Code { get; set; } = "";

    public int? DriverId { get; set; }

    public HoldPeriod Period { get; set; } = null!;
}

/// <summary>A room booked by the hour: its ends are two columns, and a booking with no room is no room's.</summary>
public sealed class RoomBooking
{
    public int Id { get; set; }

    public int? RoomId { get; set; }

    public int StartsAt { get; set; }

    public int? EndsAt { get; set; }
}

public sealed class NoOverlapDbContext(DbContextOptions<NoOverlapDbContext> options) : DbContext(options)
{
    public const string Schema = "rask_no_overlap";

    public const string ChipHeld = "A chip is held by one driver at a time.";

    public const string RoomTaken = "The room is booked then.";

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        ModelRegistry.ApplyConventions(configurationBuilder);
        configurationBuilder.OrderTextBy("Hungarian_100_CI_AS");
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        if (Database.IsNpgsql())
        {
            modelBuilder.HasDefaultSchema(Schema);
        }

        modelBuilder.Entity<ChipHold>(b =>
        {
            b.Property(h => h.Id).ValueGeneratedNever();
            b.ComplexProperty(h => h.Period);
            b.NoOverlap(h => new { h.Tenant, h.Code }, h => h.Period, ChipHeld).Where(h => h.DriverId != null);
        });

        modelBuilder.Entity<RoomBooking>(b =>
        {
            b.Property(r => r.Id).ValueGeneratedNever();
            b.NoOverlap(r => r.RoomId, r => r.StartsAt, r => r.EndsAt, RoomTaken);
        });
    }
}

/// <summary>
///     <c>b.NoOverlap(…)</c> on each provider, written once: the same rows are refused everywhere — half-open, an open end
///     unbounded, a null key and an empty range conflicting with nothing, the key compared as the app's text order says —
///     with the rule's message under the range's field, whether the save's own check or the database found it.
/// </summary>
public abstract class NoOverlapParityTests(ProviderLeg leg) : ParityTests<NoOverlapDbContext>(leg)
{
    private static readonly DateOnly Day = new(2026, 1, 1);

    [Fact]
    public async Task An_overlapping_hold_of_the_same_chip_is_refused_under_its_period()
    {
        RequireServer();
        await Hold(1, "C-1", 1, 0, 5);

        var refused = await Assert.ThrowsAsync<RaskValidationException>(() => Hold(2, "C-1", 2, 3, 7));

        Assert.Equal([NoOverlapDbContext.ChipHeld], refused.Errors["Period"]);
        Assert.Single(refused.Errors);
    }

    [Fact]
    public async Task Holds_that_meet_at_a_day_are_neighbours()
    {
        RequireServer();
        await Hold(1, "C-1", 1, 0, 5);

        await Hold(2, "C-1", 2, 5, 9);
        await Hold(3, "C-1", 3, -3, 0);

        Assert.Equal(3, await Count<ChipHold>());
    }

    [Fact]
    public async Task An_open_hold_overlaps_everything_after_its_start()
    {
        RequireServer();
        await Hold(1, "C-1", 1, 10, null);

        var later = await Assert.ThrowsAsync<RaskValidationException>(() => Hold(2, "C-1", 2, 400, 401));
        var alsoOpen = await Assert.ThrowsAsync<RaskValidationException>(() => Hold(3, "C-1", 3, 0, null));
        await Hold(4, "C-1", 4, 0, 10);

        Assert.Equal([NoOverlapDbContext.ChipHeld], later.Errors["Period"]);
        Assert.Equal([NoOverlapDbContext.ChipHeld], alsoOpen.Errors["Period"]);
    }

    [Fact]
    public async Task The_key_compares_as_the_app_s_text_order_says_and_another_key_is_no_conflict()
    {
        RequireServer();
        await Hold(1, "árvíz-1", 1, 0, 5);

        await Assert.ThrowsAsync<RaskValidationException>(() => Hold(2, "ÁRVÍZ-1", 2, 1, 2));
        await Hold(3, "arviz-1", 3, 1, 2);
        await Hold(4, "árvíz-2", 4, 1, 2);
        await Hold(5, "árvíz-1", 5, 1, 2, tenant: "other");

        Assert.Equal(4, await Count<ChipHold>());
    }

    [Fact]
    public async Task A_row_the_rule_s_filter_leaves_out_never_conflicts()
    {
        RequireServer();
        await Hold(1, "C-1", 1, 0, 5);

        await Hold(2, "C-1", driver: null, 1, 4);
        await Hold(3, "C-1", driver: null, 2, 3);

        Assert.Equal(3, await Count<ChipHold>());
    }

    [Fact]
    public async Task A_null_key_part_and_an_empty_range_conflict_with_nothing()
    {
        RequireServer();
        await Book(1, room: null, 0, 10);
        await Book(2, room: null, 2, 8);
        await Book(3, room: 7, 0, 10);

        await Book(4, room: 7, 5, 5);
        var refused = await Assert.ThrowsAsync<RaskValidationException>(() => Book(5, room: 7, 9, 12));

        Assert.Equal([NoOverlapDbContext.RoomTaken], refused.Errors["StartsAt"]);
        Assert.Equal(4, await Count<RoomBooking>());
    }

    [Fact]
    public async Task A_row_moved_onto_another_is_refused_and_a_row_is_no_conflict_with_itself()
    {
        RequireServer();
        await Book(1, room: 7, 0, 10);
        await Book(2, room: 7, 10, 20);
        await using var db = Open();

        var booking = await db.Set<RoomBooking>().SingleAsync(b => b.Id == 1, Ct);
        booking.EndsAt = 9;
        await db.SaveChangesAsync(Ct);
        booking.EndsAt = 11;
        var refused = await Assert.ThrowsAsync<RaskValidationException>(() => db.SaveChangesAsync(Ct));

        Assert.Equal([NoOverlapDbContext.RoomTaken], refused.Errors["StartsAt"]);
    }

    [Fact]
    public async Task The_database_itself_refuses_a_write_no_save_checked_first()
    {
        RequireServer();
        await Book(1, room: 7, 0, 10);
        await Book(2, room: 7, 10, 20);
        await using var db = Open();

        var refused = await Assert.ThrowsAsync<RaskValidationException>(() =>
            db.Set<RoomBooking>().Where(b => b.Id == 2).ExecuteUpdateAsync(s => s.SetProperty(b => b.StartsAt, 5), Ct));

        Assert.Equal([NoOverlapDbContext.RoomTaken], refused.Errors["StartsAt"]);
        Assert.Equal(10, await Open().Set<RoomBooking>().Where(b => b.Id == 2).Select(b => b.StartsAt).SingleAsync(Ct));
    }

    [Fact]
    public async Task Two_overlapping_saves_at_the_same_moment_never_both_get_through()
    {
        RequireServer();

        for (var round = 0; round < 8; round++)
        {
            var first = Book(100 + (round * 2), room: round, 0, 10);
            var second = Book(101 + (round * 2), room: round, 5, 15);
            var outcomes = await Task.WhenAll(Settled(first), Settled(second));

            Assert.Single(outcomes, static ok => ok);
        }

        Assert.Equal(8, await Count<RoomBooking>());
    }

    private protected override NoOverlapDbContext Open() => Leg.Open<NoOverlapDbContext>(o => new NoOverlapDbContext(o));

    private static async Task<bool> Settled(Task save)
    {
        try
        {
            await save;
            return true;
        }
        catch (Exception refused) when (refused is RaskValidationException or DbUpdateException or InvalidOperationException)
        {
            // Refused by the rule — or, on SQL Server, chosen as the deadlock's victim: either way not written.
            return false;
        }
    }

    private async Task Hold(int id, string code, int? driver, int from, int? to, string tenant = "t")
    {
        await using var db = Open();
        db.Add(new ChipHold
        {
            Id = id,
            Tenant = tenant,
            Code = code,
            DriverId = driver,
            Period = new HoldPeriod(Day.AddDays(from), to is { } end ? Day.AddDays(end) : null),
        });
        await db.SaveChangesAsync(Ct);
    }

    private async Task Book(int id, int? room, int startsAt, int? endsAt)
    {
        await using var db = Open();
        db.Add(new RoomBooking { Id = id, RoomId = room, StartsAt = startsAt, EndsAt = endsAt });
        await db.SaveChangesAsync(Ct);
    }

    private async Task<int> Count<T>()
        where T : class
    {
        await using var db = Open();
        return await db.Set<T>().CountAsync(Ct);
    }
}

public sealed class SqliteNoOverlapParityTests() : NoOverlapParityTests(ProviderLeg.Sqlite("no-overlap"));

[Collection(SqlServerCollection.Name)]
public sealed class SqlServerNoOverlapParityTests() : NoOverlapParityTests(ProviderLeg.SqlServer("rask_e2e_no_overlap"));

[Collection(PostgresCollection.Name)]
public sealed class PostgresNoOverlapParityTests() : NoOverlapParityTests(ProviderLeg.Postgres(NoOverlapDbContext.Schema));
