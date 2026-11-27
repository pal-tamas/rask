using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Rask.Data;

namespace Rask.SqlServer.Tests;

/// <summary>A desk booked by the hour; a booking without a desk is no desk's.</summary>
public sealed class DeskBooking
{
    public int Id { get; set; }

    public int? DeskId { get; set; }

    public long StartsAt { get; set; }

    public long? EndsAt { get; set; }

    public bool Cancelled { get; set; }
}

public sealed class DeskBookingContext(DbContextOptions<DeskBookingContext> options) : RaskDbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<DeskBooking>()
            .NoOverlap(b => b.DeskId, b => b.StartsAt, b => b.EndsAt, "The desk is booked then.")
            .Where(b => !b.Cancelled);
    }
}

/// <summary><c>b.NoOverlap(…)</c> on SQL Server, as the schema script writes it: an index and a trigger.</summary>
public sealed class NoOverlapScriptTests
{
    [Fact]
    public void The_schema_holds_the_rule_by_an_index_and_a_trigger_that_throws_its_name()
    {
        using var context = new DeskBookingContext(Options());

        var script = context.Database.GenerateCreateScript();

        Assert.Contains("CREATE INDEX [ix_desk_bookings_desk_id_starts_at] ON [desk_bookings] ([desk_id], [starts_at]);", script, StringComparison.Ordinal);
        Assert.Contains("CREATE TRIGGER [tr_desk_bookings_desk_id_starts_at] ON [desk_bookings] AFTER INSERT, UPDATE AS", script, StringComparison.Ordinal);
        Assert.Contains("JOIN (SELECT * FROM [desk_bookings] WITH (UPDLOCK, HOLDLOCK) WHERE [cancelled]", script, StringComparison.Ordinal);
        Assert.Contains("AS x ON x.[desk_id] = n.[desk_id]", script, StringComparison.Ordinal);
        Assert.Contains("THROW 51101, N'xc_desk_bookings_desk_id_starts_at', 1;", script, StringComparison.Ordinal);
    }

    [Fact]
    public void The_model_tells_EF_Core_the_table_has_a_trigger()
    {
        using var context = new DeskBookingContext(Options());

        var entity = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(DeskBooking))!;

        Assert.Equal(["tr_desk_bookings_desk_id_starts_at"], entity.GetDeclaredTriggers().Select(static t => t.ModelName));
    }

    private static DbContextOptions<DeskBookingContext> Options() =>
        new DbContextOptionsBuilder<DeskBookingContext>()
            .UseRaskSqlServer(ConfigurationServices.With(new() { ["Rask:ConnectionStrings:App"] = "Server=unused;Database=unused" }))
            .Options;
}
