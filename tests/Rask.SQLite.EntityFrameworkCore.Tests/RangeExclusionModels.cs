using Microsoft.EntityFrameworkCore;
using Rask.Data;

namespace Rask.SQLite.EntityFrameworkCore.Tests;

// Test models for RaskSqliteRangeExclusionTests. EF caches a built model per (context type, options), so each
// model variant needs its own context type — two instances of one type would silently share a cached model.
internal sealed class Booking
{
    public int Id { get; set; }

    public int RoomId { get; set; }

    public long StartsAt { get; set; }

    public long EndsAt { get; set; }

    public string? Note { get; set; }
}

internal class BookingContext(DbContextOptions options) : DbContext(options)
{
    public DbSet<Booking> Bookings => Set<Booking>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Booking>()
            .HasNonOverlappingRange(x => x.StartsAt, x => x.EndsAt, partitionBy: x => x.RoomId);
    }
}

// The Bookings table with no rule at all — the "before" of adding one, and the "after" of removing it (#1113).
internal sealed class UnruledBookingContext(DbContextOptions options) : DbContext(options)
{
    public DbSet<Booking> Bookings => Set<Booking>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.Entity<Booking>();
}

// The same rule without the partition: every room shares one calendar, so the index's columns change.
internal sealed class UnpartitionedBookingContext(DbContextOptions options) : DbContext(options)
{
    public DbSet<Booking> Bookings => Set<Booking>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<Booking>().HasNonOverlappingRange(x => x.StartsAt, x => x.EndsAt);
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

internal sealed class Season
{
    public int Id { get; set; }

    public string ValidFrom { get; set; } = string.Empty;

    public string ValidTo { get; set; } = string.Empty;
}

internal sealed class SeasonContext(DbContextOptions options) : DbContext(options)
{
    public DbSet<Season> Seasons => Set<Season>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // No partition: the rule covers the whole table.
        modelBuilder.Entity<Season>().HasNonOverlappingRange(x => x.ValidFrom, x => x.ValidTo);
    }
}

internal sealed class Slot
{
    public int Id { get; set; }

    public long StartsAt { get; set; }

    public long EndsAt { get; set; }
}

internal sealed class SlotContext(DbContextOptions options) : DbContext(options)
{
    public DbSet<Slot> Slots => Set<Slot>();
}

internal sealed class RenamedColumnContext(DbContextOptions options) : DbContext(options)
{
    public DbSet<Slot> Slots => Set<Slot>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var slot = modelBuilder.Entity<Slot>();
        slot.Property(x => x.StartsAt).HasColumnName("from");
        slot.Property(x => x.EndsAt).HasColumnName("to");
        slot.HasNonOverlappingRange(x => x.StartsAt, x => x.EndsAt);
    }
}

internal sealed class Lease : Aggregate<int>
{
    // This suite is about a soft-deleted row freeing its range, so it opts in.
    public const Deletion Deletes = Deletion.Soft;

    public int AssetId { get; private set; }

    public long StartsAt { get; private set; }

    public long EndsAt { get; private set; }

    public static Lease For(int id, int assetId, long startsAt, long endsAt) =>
        new() { Id = id, AssetId = assetId, StartsAt = startsAt, EndsAt = endsAt };
}

internal sealed class LeaseContext(DbContextOptions options) : DbContext(options)
{
    public DbSet<Lease> Leases => Set<Lease>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Lease>()
            .HasNonOverlappingRange(x => x.StartsAt, x => x.EndsAt, partitionBy: x => x.AssetId);

        modelBuilder.ApplyRaskConventions();
    }
}

// The default aggregate: hard delete, so DeletedAt is ignored and the rule has nothing to filter on (#1131).
internal sealed class Reservation : Aggregate<int>
{
    public int DeskId { get; private set; }

    public long StartsAt { get; private set; }

    public long EndsAt { get; private set; }

    public static Reservation For(int id, int deskId, long startsAt, long endsAt) =>
        new() { Id = id, DeskId = deskId, StartsAt = startsAt, EndsAt = endsAt };
}

internal sealed class ReservationContext(DbContextOptions options) : DbContext(options)
{
    public DbSet<Reservation> Reservations => Set<Reservation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Reservation>()
            .HasNonOverlappingRange(x => x.StartsAt, x => x.EndsAt, partitionBy: x => x.DeskId);

        modelBuilder.ApplyRaskConventions();
    }
}

// A rule annotation that asks to ignore soft-deleted rows on a table with no DeletedAt — what an older build wrote
// for a hard-delete aggregate (#1131). The builder refuses the flag now, so the annotation is set by hand.
internal sealed class StaleSoftDeleteRuleContext(DbContextOptions options) : DbContext(options)
{
    public DbSet<Booking> Bookings => Set<Booking>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<Booking>().HasAnnotation(
            RangeExclusionSpec.AnnotationName,
            new RangeExclusionSpec("StartsAt", "EndsAt", ["RoomId"], IgnoreSoftDeleted: true).Serialize());
}

internal sealed class Meeting
{
    // No explicit value on insert: SQLite assigns the rowid.
    public int Id { get; set; }

    public int RoomId { get; set; }

    public long StartsAt { get; set; }

    public long EndsAt { get; set; }
}

internal sealed class GeneratedKeyContext(DbContextOptions options) : DbContext(options)
{
    public DbSet<Meeting> Meetings => Set<Meeting>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Meeting>()
            .HasNonOverlappingRange(x => x.StartsAt, x => x.EndsAt, partitionBy: x => x.RoomId);
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

internal sealed class CompositeKeyContext(DbContextOptions options) : DbContext(options)
{
    public DbSet<Shift> Shifts => Set<Shift>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var shift = modelBuilder.Entity<Shift>();
        shift.HasKey(x => new { x.TenantId, x.Code });
        shift.HasNonOverlappingRange(x => x.StartsAt, x => x.EndsAt, partitionBy: x => x.EmployeeId);
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
