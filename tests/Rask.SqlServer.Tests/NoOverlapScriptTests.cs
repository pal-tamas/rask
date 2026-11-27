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

/// <summary>A locker lent by the day, keyed by a site named in a text of no length: nvarchar(max).</summary>
public sealed class LockerLoan
{
    public int Id { get; set; }

    public string Site { get; set; } = "";

    public int LockerNo { get; set; }

    public DateOnly From { get; set; }

    public DateOnly? To { get; set; }
}

public sealed class LockerLoanContext(DbContextOptions<LockerLoanContext> options) : RaskDbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<LockerLoan>()
            .NoOverlap(l => new { l.Site, l.LockerNo }, l => l.From, l => l.To, "The locker is lent then.");
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

        Assert.Contains("CREATE INDEX [IX_DeskBooking_DeskId_StartsAt] ON [DeskBooking] ([DeskId], [StartsAt]);", script, StringComparison.Ordinal);
        Assert.Contains("CREATE TRIGGER [TR_DeskBooking_DeskId_StartsAt] ON [DeskBooking] AFTER INSERT, UPDATE AS", script, StringComparison.Ordinal);
        Assert.Contains("JOIN (SELECT * FROM [DeskBooking] WITH (UPDLOCK, HOLDLOCK) WHERE [Cancelled]", script, StringComparison.Ordinal);
        Assert.Contains("AS x ON x.[DeskId] = n.[DeskId]", script, StringComparison.Ordinal);
        Assert.Contains("THROW 51101, N'EX_DeskBooking_DeskId_StartsAt', 1;", script, StringComparison.Ordinal);
    }

    [Fact]
    public void A_key_of_a_max_type_is_carried_by_the_index_not_keyed_by_it()
    {
        using var context = new LockerLoanContext(Options<LockerLoanContext>());

        var script = context.Database.GenerateCreateScript();

        Assert.Contains("[Site] nvarchar(max) NOT NULL", script, StringComparison.Ordinal);
        Assert.Contains("CREATE INDEX [IX_LockerLoan_Site_LockerNo_From] ON [LockerLoan] ([LockerNo], [From]) INCLUDE ([Site]);", script, StringComparison.Ordinal);
        Assert.Contains("AS x ON x.[Site] = n.[Site] AND x.[LockerNo] = n.[LockerNo]", script, StringComparison.Ordinal);
    }

    [Fact]
    public void The_model_tells_EF_Core_the_table_has_a_trigger()
    {
        using var context = new DeskBookingContext(Options());

        var entity = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(DeskBooking))!;

        Assert.Equal(["TR_DeskBooking_DeskId_StartsAt"], entity.GetDeclaredTriggers().Select(static t => t.ModelName));
    }

    private static DbContextOptions<DeskBookingContext> Options() => Options<DeskBookingContext>();

    private static DbContextOptions<TContext> Options<TContext>()
        where TContext : DbContext =>
        new DbContextOptionsBuilder<TContext>()
            .UseRaskSqlServer(ConfigurationServices.With(new() { ["Rask:ConnectionStrings:App"] = "Server=unused;Database=unused" }))
            .Options;
}
