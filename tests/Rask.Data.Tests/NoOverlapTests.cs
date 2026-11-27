using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Rask.Data.Tests;

// Soft-deletable because it asks to be: its rule holds the live rows only.
public sealed class OverlapLease : Aggregate<int, Timestamps, Versioned, Model, SoftDelete>
{
    public int AssetId { get; private set; }

    public long StartsAt { get; private set; }

    public long EndsAt { get; private set; }
}

// The default aggregate: hard delete, so there is no DeletedAt for the rule to filter on.
public sealed class OverlapDesk : Aggregate<int, Timestamps, Versioned, Model, Deletable>
{
    public int RoomId { get; private set; }

    public long StartsAt { get; private set; }

    public long EndsAt { get; private set; }
}

// What b.NoOverlap(…) leaves on the model, resolved to columns as the model is finalized: the half every provider's
// DDL, the save's own check and the refusal's translation read.
public sealed class NoOverlapTests
{
    [Fact]
    public void A_rule_over_two_properties_is_carried_as_its_columns_and_its_message()
    {
        var model = BuildModel(b => b.Entity<Booking>().NoOverlap(x => new { x.RoomId, x.Wing }, x => x.StartsAt, x => x.EndsAt, "Taken"));

        var rule = Assert.Single(NoOverlapRule.Of(model.FindEntityType(typeof(Booking))!));

        Assert.Equal("EX_Booking_RoomId_Wing_StartsAt", rule.Name);
        Assert.Equal(["RoomId", "Wing"], rule.Keys);
        Assert.Equal(("StartsAt", "EndsAt"), (rule.From, rule.To));
        Assert.Equal(("Taken", "StartsAt"), (rule.Message, rule.Field));
        Assert.Equal("", rule.Rows);
    }

    [Fact]
    public void A_range_value_object_s_ends_are_the_two_members_of_its_record()
    {
        var model = BuildModel(b => b.Entity<Booking>(e =>
        {
            e.ComplexProperty(x => x.Period);
            e.NoOverlap(x => x.RoomId, x => x.Period, "Taken");
        }));

        var rule = Assert.Single(NoOverlapRule.Of(model.FindEntityType(typeof(Booking))!));

        Assert.Equal(("Period_From", "Period_To"), (rule.From, rule.To));
        Assert.Equal(("Period.From", "Period.To", "Period"), (rule.FromPath, rule.ToPath, rule.Field));
    }

    [Fact]
    public void A_where_is_written_as_the_provider_s_own_filter()
    {
        var model = BuildModel(b => b.Entity<Booking>()
            .NoOverlap(x => x.RoomId, x => x.StartsAt, x => x.EndsAt, "Taken")
            .Where(x => x.Wing != null));

        var rule = Assert.Single(NoOverlapRule.Of(model.FindEntityType(typeof(Booking))!));

        Assert.Equal("\"Wing\" IS NOT NULL", rule.Rows);
    }

    [Fact]
    public void A_soft_deleting_aggregate_s_rule_holds_its_live_rows_and_a_hard_deleting_one_s_every_row()
    {
        var model = BuildModel(b =>
        {
            b.Entity<OverlapLease>().NoOverlap(x => x.AssetId, x => x.StartsAt, x => x.EndsAt, "Leased");
            b.Entity<OverlapDesk>().NoOverlap(x => x.RoomId, x => x.StartsAt, x => x.EndsAt, "Booked");
            b.ApplyRaskConventions();
        });

        var lease = Assert.Single(NoOverlapRule.Of(model.FindEntityType(typeof(OverlapLease))!));
        var desk = Assert.Single(NoOverlapRule.Of(model.FindEntityType(typeof(OverlapDesk))!));

        Assert.Equal("\"DeletedAt\" IS NULL", lease.Rows);
        Assert.Equal("", desk.Rows);
    }

    [Fact]
    public void Several_rules_on_one_entity_each_get_a_name_of_their_own()
    {
        var model = BuildModel(b => b.Entity<Booking>(e =>
        {
            e.NoOverlap(x => x.RoomId, x => x.StartsAt, x => x.EndsAt, "Room taken");
            e.NoOverlap(x => x.RoomId, x => x.StartsAt, x => x.EndsAt, "Again").Where(x => x.Wing != null);
        }));

        var rules = NoOverlapRule.Of(model.FindEntityType(typeof(Booking))!);

        Assert.Equal(["EX_Booking_RoomId_StartsAt", "EX_Booking_RoomId_StartsAt_2"], rules.Select(r => r.Name));
    }

    [Fact]
    public void The_rules_round_trip_through_the_annotation_s_text()
    {
        NoOverlapRule[] rules = [new("EX_T_K_F", ["K"], ["Key"], "F", "Span.From", "T", "Span.To", "\"A\" = 'x|y'", "No, \"quoted\".", "Span")];

        var read = NoOverlapRule.Parse(NoOverlapRule.Write(rules));

        Assert.Equal(rules, read);
    }

    [Fact]
    public void An_expression_that_names_no_property_of_the_entity_is_refused()
    {
        var error = Assert.Throws<ArgumentException>(() =>
            BuildModel(b => b.Entity<Booking>().NoOverlap(x => x.RoomId + 1, x => x.StartsAt, x => x.EndsAt, "Taken")));

        Assert.Equal("key", error.ParamName);
    }

    [Fact]
    public void A_range_that_is_no_value_object_of_its_own_columns_is_refused_as_the_model_is_built()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            BuildModel(b => b.Entity<Booking>().NoOverlap(x => x.RoomId, x => x.StartsAt, "Taken")));

        Assert.Contains("is no Range value object of its own columns", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_entity_that_declares_no_rule_carries_none()
    {
        var model = BuildModel(b => b.Entity<Booking>());

        Assert.Empty(NoOverlapRule.Of(model.FindEntityType(typeof(Booking))!));
    }

    private static IModel BuildModel(Action<ModelBuilder> configure)
    {
        var options = OptionsLayout.LikeAddDbContext<ProbeContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;

        using var context = new ProbeContext(options, configure);
        return context.Model;
    }

    private sealed record Span(long From, long? To);

    private sealed class Booking
    {
        public int Id { get; set; }

        public int RoomId { get; set; }

        public string? Wing { get; set; }

        public long StartsAt { get; set; }

        public long EndsAt { get; set; }

        public Span Period { get; set; } = null!;
    }

    // Each test builds a one-off model; EF caches one per context type and options otherwise.
    private sealed class ProbeContext(DbContextOptions<ProbeContext> options, Action<ModelBuilder> configure)
        : DbContext(options)
    {
        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
            ModelRegistry.ApplyConventions(configurationBuilder);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Booking>().Ignore(x => x.Period);
            configure(modelBuilder);
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            => optionsBuilder.ReplaceService<IModelCacheKeyFactory, EachContextItsOwnModel>();
    }
}
