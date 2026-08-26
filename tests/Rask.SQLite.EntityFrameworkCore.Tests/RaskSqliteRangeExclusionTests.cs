using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Rask.Data;

namespace Rask.SQLite.EntityFrameworkCore.Tests;

// Integration tests for HasNonOverlappingRange, driven through the same migration path `dotnet ef` uses
// (model differ -> IMigrationsSqlGenerator) against a real SQLite database file.
public sealed class RaskSqliteRangeExclusionTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-range-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task Overlapping_range_is_rejected()
    {
        await using var context = Create<BookingContext>();
        CreateSchema(context);

        context.Bookings.Add(new Booking { Id = 1, RoomId = 1, StartsAt = 100, EndsAt = 200 });
        await context.SaveChangesAsync();

        context.Bookings.Add(new Booking { Id = 2, RoomId = 1, StartsAt = 150, EndsAt = 250 });

        var error = await Assert.ThrowsAsync<RangeOverlapException>(() => context.SaveChangesAsync());
        Assert.Equal("Bookings", error.Table);
    }

    [Fact]
    public async Task Adjacent_ranges_are_allowed_because_the_range_is_half_open()
    {
        await using var context = Create<BookingContext>();
        CreateSchema(context);

        context.Bookings.Add(new Booking { Id = 1, RoomId = 1, StartsAt = 100, EndsAt = 200 });
        context.Bookings.Add(new Booking { Id = 2, RoomId = 1, StartsAt = 200, EndsAt = 300 });

        await context.SaveChangesAsync();

        Assert.Equal(2, await context.Bookings.CountAsync());
    }

    [Theory]
    [InlineData(120, 130)] // fully contained
    [InlineData(50, 500)]  // fully enclosing
    [InlineData(100, 200)] // exact duplicate
    [InlineData(150, 250)] // straddles the end
    [InlineData(50, 150)]  // straddles the start
    public async Task Every_shape_of_overlap_is_rejected(long startsAt, long endsAt)
    {
        await using var context = Create<BookingContext>();
        CreateSchema(context);

        context.Bookings.Add(new Booking { Id = 1, RoomId = 1, StartsAt = 100, EndsAt = 200 });
        await context.SaveChangesAsync();

        context.Bookings.Add(new Booking { Id = 2, RoomId = 1, StartsAt = startsAt, EndsAt = endsAt });

        await Assert.ThrowsAsync<RangeOverlapException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task The_rule_is_scoped_to_its_partition()
    {
        await using var context = Create<BookingContext>();
        CreateSchema(context);

        context.Bookings.Add(new Booking { Id = 1, RoomId = 1, StartsAt = 100, EndsAt = 200 });
        context.Bookings.Add(new Booking { Id = 2, RoomId = 2, StartsAt = 100, EndsAt = 200 });

        await context.SaveChangesAsync();

        Assert.Equal(2, await context.Bookings.CountAsync());
    }

    [Fact]
    public async Task A_row_does_not_conflict_with_itself_on_update()
    {
        await using var context = Create<BookingContext>();
        CreateSchema(context);

        var booking = new Booking { Id = 1, RoomId = 1, StartsAt = 100, EndsAt = 200 };
        context.Bookings.Add(booking);
        await context.SaveChangesAsync();

        booking.EndsAt = 400;
        await context.SaveChangesAsync();

        Assert.Equal(400, (await context.Bookings.SingleAsync()).EndsAt);
    }

    [Fact]
    public async Task Moving_a_row_onto_a_neighbour_is_rejected()
    {
        await using var context = Create<BookingContext>();
        CreateSchema(context);

        var first = new Booking { Id = 1, RoomId = 1, StartsAt = 100, EndsAt = 200 };
        context.Bookings.Add(first);
        context.Bookings.Add(new Booking { Id = 2, RoomId = 1, StartsAt = 200, EndsAt = 300 });
        await context.SaveChangesAsync();

        first.EndsAt = 250;

        await Assert.ThrowsAsync<RangeOverlapException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task The_rule_holds_against_writes_that_bypass_the_DbContext()
    {
        await using var context = Create<BookingContext>();
        CreateSchema(context);

        context.Bookings.Add(new Booking { Id = 1, RoomId = 1, StartsAt = 100, EndsAt = 200 });
        await context.SaveChangesAsync();

        // The constraint lives in the database, so raw SQL is bound by it too.
        var error = await Assert.ThrowsAsync<SqliteException>(() => context.Database.ExecuteSqlRawAsync(
            """INSERT INTO "Bookings" ("Id","RoomId","StartsAt","EndsAt") VALUES (2,1,150,250);"""));

        Assert.Equal(1811, error.SqliteExtendedErrorCode);
    }

    [Fact]
    public async Task A_table_wide_rule_needs_no_partition()
    {
        await using var context = Create<SeasonContext>();
        CreateSchema(context);

        context.Seasons.Add(new Season { Id = 1, ValidFrom = "2026-01-01", ValidTo = "2026-04-01" });
        await context.SaveChangesAsync();

        context.Seasons.Add(new Season { Id = 2, ValidFrom = "2026-03-01", ValidTo = "2026-05-01" });

        await Assert.ThrowsAsync<RangeOverlapException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task A_renamed_column_is_honoured()
    {
        await using var context = Create<RenamedColumnContext>();
        CreateSchema(context);

        // The rule was declared over the properties; the DDL must use the mapped column names.
        Assert.Contains("\"from\"", Ddl(context), StringComparison.Ordinal);

        context.Slots.Add(new Slot { Id = 1, StartsAt = 100, EndsAt = 200 });
        await context.SaveChangesAsync();

        context.Slots.Add(new Slot { Id = 2, StartsAt = 150, EndsAt = 250 });

        await Assert.ThrowsAsync<RangeOverlapException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task A_soft_deleted_row_frees_its_slot()
    {
        await using var context = Create<LeaseContext>();
        CreateSchema(context);

        var lease = new Lease { Id = 1, AssetId = 1, StartsAt = 100, EndsAt = 200 };
        context.Leases.Add(lease);
        await context.SaveChangesAsync();

        lease.DeletedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await context.SaveChangesAsync();

        context.Leases.Add(new Lease { Id = 2, AssetId = 1, StartsAt = 100, EndsAt = 200 });
        await context.SaveChangesAsync();

        Assert.Equal(2, await context.Leases.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task The_constraint_survives_a_migration_that_rebuilds_the_table()
    {
        // SQLite cannot ALTER most things in place: EF rebuilds the table and DROPs the original, taking its
        // triggers with it. The generator must put them back, or the constraint silently disappears.
        await using (var before = Create<BookingContext>())
        {
            CreateSchema(before);
            before.Bookings.Add(new Booking { Id = 1, RoomId = 1, StartsAt = 100, EndsAt = 200 });
            await before.SaveChangesAsync();
        }

        await using var after = Create<RebuiltBookingContext>();
        Migrate<BookingContext>(after);

        Assert.Equal(2, TriggerCount(after));

        after.Bookings.Add(new Booking { Id = 2, RoomId = 1, StartsAt = 150, EndsAt = 250 });
        await Assert.ThrowsAsync<RangeOverlapException>(() => after.SaveChangesAsync());

        after.ChangeTracker.Clear();
        after.Bookings.Add(new Booking { Id = 3, RoomId = 1, StartsAt = 200, EndsAt = 300 });
        await after.SaveChangesAsync();
    }

    [Fact]
    public async Task A_store_generated_key_is_handled()
    {
        // The key is assigned by SQLite, so NEW.<key> is still NULL when the BEFORE INSERT trigger runs and
        // the "not this same row" test has to hold anyway.
        await using var context = Create<GeneratedKeyContext>();
        CreateSchema(context);

        context.Meetings.Add(new Meeting { RoomId = 1, StartsAt = 100, EndsAt = 200 });
        await context.SaveChangesAsync();

        Assert.NotEqual(0, (await context.Meetings.SingleAsync()).Id);

        context.Meetings.Add(new Meeting { RoomId = 1, StartsAt = 150, EndsAt = 250 });
        await Assert.ThrowsAsync<RangeOverlapException>(() => context.SaveChangesAsync());

        context.ChangeTracker.Clear();
        context.Meetings.Add(new Meeting { RoomId = 1, StartsAt = 200, EndsAt = 300 });
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task A_composite_primary_key_is_handled()
    {
        await using var context = Create<CompositeKeyContext>();
        CreateSchema(context);

        var shift = new Shift { TenantId = 1, Code = "a", EmployeeId = 7, StartsAt = 100, EndsAt = 200 };
        context.Shifts.Add(shift);
        await context.SaveChangesAsync();

        // Widening its own range must not collide with itself across a two-column key.
        shift.EndsAt = 300;
        await context.SaveChangesAsync();

        context.Shifts.Add(new Shift { TenantId = 1, Code = "b", EmployeeId = 7, StartsAt = 250, EndsAt = 400 });
        await Assert.ThrowsAsync<RangeOverlapException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Strict_tables_and_the_range_rule_hold_at_the_same_time()
    {
        // EF Core resolves exactly one IMigrationsSqlGenerator. Registering a strict generator and a
        // range-exclusion generator separately keeps only the last replacement, so one of the two features
        // disappears with nothing failing — this pins that they compose instead.
        await using var context = Create<BookingContext>(strictTables: true);
        CreateSchema(context);

        Assert.Contains("STRICT", Ddl(context), StringComparison.Ordinal);
        Assert.Equal(2, TriggerCount(context));

        context.Bookings.Add(new Booking { Id = 1, RoomId = 1, StartsAt = 100, EndsAt = 200 });
        await context.SaveChangesAsync();

        // The range rule still bites ...
        context.Bookings.Add(new Booking { Id = 2, RoomId = 1, StartsAt = 150, EndsAt = 250 });
        await Assert.ThrowsAsync<RangeOverlapException>(() => context.SaveChangesAsync());

        context.ChangeTracker.Clear();

        // ... and so does strictness, which a non-STRICT table would happily accept.
        var error = await Assert.ThrowsAsync<SqliteException>(() => context.Database.ExecuteSqlRawAsync(
            """UPDATE "Bookings" SET "RoomId" = 'lots' WHERE "Id" = 1;"""));

        Assert.Contains("cannot store TEXT value in INTEGER column", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_strict_tables_the_range_rule_still_holds()
    {
        await using var context = Create<BookingContext>();
        CreateSchema(context);

        Assert.DoesNotContain("STRICT", Ddl(context), StringComparison.Ordinal);
        Assert.Equal(2, TriggerCount(context));

        context.Bookings.Add(new Booking { Id = 1, RoomId = 1, StartsAt = 100, EndsAt = 200 });
        await context.SaveChangesAsync();

        context.Bookings.Add(new Booking { Id = 2, RoomId = 1, StartsAt = 150, EndsAt = 250 });
        await Assert.ThrowsAsync<RangeOverlapException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task An_entity_without_the_rule_gets_no_triggers()
    {
        await using var context = Create<PlainContext>();
        CreateSchema(context);

        Assert.Equal(0, TriggerCount(context));

        context.Notes.Add(new Note { Id = 1, Text = "a" });
        await context.SaveChangesAsync();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        File.Delete(_dbPath);
    }

    private TContext Create<TContext>(bool strictTables = false)
        where TContext : DbContext
    {
        var options = new DbContextOptionsBuilder<TContext>()
            .UseRaskSqlite($"Data Source={_dbPath}", strictTables: strictTables)
            .Options;

        return (TContext)Activator.CreateInstance(typeof(TContext), options)!;
    }

    // Runs the model through the differ and generator — the same path `dotnet ef database update` takes.
    private static void CreateSchema(DbContext context) => Migrate(context, null);

    private void Migrate<TFrom>(DbContext context)
        where TFrom : DbContext
    {
        var options = new DbContextOptionsBuilder<TFrom>().UseRaskSqlite($"Data Source={_dbPath}").Options;
        using var from = (TFrom)Activator.CreateInstance(typeof(TFrom), options)!;
        Migrate(context, from);
    }

    private static void Migrate(DbContext context, DbContext? from)
    {
        var target = context.GetService<IDesignTimeModel>().Model;
        var source = from?.GetService<IDesignTimeModel>().Model.GetRelationalModel();

        var operations = context.GetService<IMigrationsModelDiffer>()
            .GetDifferences(source, target.GetRelationalModel());

        foreach (var command in context.GetService<IMigrationsSqlGenerator>().Generate(operations, target))
        {
            context.Database.ExecuteSqlRaw(command.CommandText);
        }
    }

    private static string Ddl(DbContext context)
        => string.Join(
            "\n",
            context.Database.SqlQueryRaw<string>("SELECT COALESCE(sql, '') AS Value FROM sqlite_master").ToList());

    private static int TriggerCount(DbContext context)
        => context.Database
            .SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM sqlite_master WHERE type = 'trigger'")
            .AsEnumerable()
            .Single();
}
