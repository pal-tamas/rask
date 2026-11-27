using Microsoft.EntityFrameworkCore;
using Rask.Data;

namespace Rask.SQLite.EntityFrameworkCore.Tests;

// Test models for RaskSqliteNoOverlapTests. EF caches a built model per (context type, options), so each
// model variant needs its own context type — two instances of one type would silently share a cached model.
internal sealed class Booking
{
    public int Id { get; set; }

    public int RoomId { get; set; }

    public long StartsAt { get; set; }

    public long EndsAt { get; set; }

    public string? Note { get; set; }

    public int Building { get; set; }
}

// NoOverlap is resolved by Rask's conventions, which a RaskDbContext applies by itself.
internal abstract class RuledContext(DbContextOptions options) : DbContext(options)
{
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        ModelRegistry.ApplyConventions(configurationBuilder);
}

internal class BookingContext(DbContextOptions options) : RuledContext(options)
{
    public DbSet<Booking> Bookings => Set<Booking>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Booking>()
            .NoOverlap(x => x.RoomId, x => x.StartsAt, x => x.EndsAt, "Taken");
    }
}

// The bookings table, named as the ruled ones are, with no rule at all — the "before" of adding one, and the "after" of removing it (#1113).
internal sealed class UnruledBookingContext(DbContextOptions options) : RuledContext(options)
{
    public DbSet<Booking> Bookings => Set<Booking>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.Entity<Booking>();
}

// The same rule over the building: every room of it shares one calendar, so the index's columns change.
internal sealed class UnpartitionedBookingContext(DbContextOptions options) : RuledContext(options)
{
    public DbSet<Booking> Bookings => Set<Booking>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<Booking>().NoOverlap(x => x.Building, x => x.StartsAt, x => x.EndsAt, "Taken");
}

// Same table, but Note becomes required — a change SQLite cannot apply in place, so EF rebuilds the table.
internal sealed class RebuiltBookingContext(DbContextOptions options) : BookingContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<Booking>().Property(x => x.Note).IsRequired().HasDefaultValue(string.Empty);
    }
}

internal sealed class Slot
{
    public int Id { get; set; }

    public int Lane { get; set; }

    public long StartsAt { get; set; }

    public long EndsAt { get; set; }
}

internal sealed class SlotContext(DbContextOptions options) : DbContext(options)
{
    public DbSet<Slot> Slots => Set<Slot>();
}

internal sealed class RenamedColumnContext(DbContextOptions options) : RuledContext(options)
{
    public DbSet<Slot> Slots => Set<Slot>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var slot = modelBuilder.Entity<Slot>();
        slot.Property(x => x.StartsAt).HasColumnName("from");
        slot.Property(x => x.EndsAt).HasColumnName("to");
        slot.NoOverlap(x => x.Lane, x => x.StartsAt, x => x.EndsAt, "Taken");
    }
}

internal sealed class Lease : Aggregate<int>, IHasTimestamps, IVersioned, IHasForm, ISoftDeletes
{
    public int AssetId { get; private set; }

    public long StartsAt { get; private set; }

    public long EndsAt { get; private set; }

    public static Lease For(int id, int assetId, long startsAt, long endsAt) =>
        new() { Id = id, AssetId = assetId, StartsAt = startsAt, EndsAt = endsAt };
}

internal sealed class LeaseContext(DbContextOptions options) : RuledContext(options)
{
    public DbSet<Lease> Leases => Set<Lease>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Lease>()
            .NoOverlap(x => x.AssetId, x => x.StartsAt, x => x.EndsAt, "Taken");

        modelBuilder.ApplyRaskConventions();
    }
}

// The default aggregate: hard delete, so DeletedAt is ignored and the rule has nothing to filter on (#1131).
internal sealed class Reservation : Aggregate<int>, IHasTimestamps, IVersioned, IHasForm, IDeletable
{
    public int DeskId { get; private set; }

    public long StartsAt { get; private set; }

    public long EndsAt { get; private set; }

    public static Reservation For(int id, int deskId, long startsAt, long endsAt) =>
        new() { Id = id, DeskId = deskId, StartsAt = startsAt, EndsAt = endsAt };
}

internal sealed class ReservationContext(DbContextOptions options) : RuledContext(options)
{
    public DbSet<Reservation> Reservations => Set<Reservation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Reservation>()
            .NoOverlap(x => x.DeskId, x => x.StartsAt, x => x.EndsAt, "Taken");

        modelBuilder.ApplyRaskConventions();
    }
}

internal sealed class Meeting
{
    // No explicit value on insert: SQLite assigns the rowid.
    public int Id { get; set; }

    public int RoomId { get; set; }

    public long StartsAt { get; set; }

    public long EndsAt { get; set; }
}

internal sealed class GeneratedKeyContext(DbContextOptions options) : RuledContext(options)
{
    public DbSet<Meeting> Meetings => Set<Meeting>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Meeting>()
            .NoOverlap(x => x.RoomId, x => x.StartsAt, x => x.EndsAt, "Taken");
    }
}

internal sealed class Shift
{
    public int TenantId { get; set; }

    public string Code { get; set; } = string.Empty;

    public int EmployeeId { get; set; }

    public long StartsAt { get; set; }

    public long EndsAt { get; set; }
}

internal sealed class CompositeKeyContext(DbContextOptions options) : RuledContext(options)
{
    public DbSet<Shift> Shifts => Set<Shift>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var shift = modelBuilder.Entity<Shift>();
        shift.HasKey(x => new { x.TenantId, x.Code });
        shift.NoOverlap(x => x.EmployeeId, x => x.StartsAt, x => x.EndsAt, "Taken");
    }
}

internal sealed class Note
{
    public int Id { get; set; }

    public string Text { get; set; } = string.Empty;
}

internal sealed class PlainContext(DbContextOptions options) : DbContext(options)
{
    public DbSet<Note> Notes => Set<Note>();
}
