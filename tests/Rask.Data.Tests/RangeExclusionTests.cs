using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Rask.Data.Tests;

// Unit tests for the provider-agnostic half of HasNonOverlappingRange: what lands on the model, and the
// spec's round-trip through the string form migrations and model snapshots store it as.
[Collection(DataDbCollection.Name)]
public sealed class RangeExclusionTests
{
    [Fact]
    public void The_spec_round_trips_through_its_serialized_form()
    {
        var spec = new RangeExclusionSpec("StartsAt", "EndsAt", ["RoomId", "Wing"], IgnoreSoftDeleted: true);

        Assert.True(RangeExclusionSpec.TryParse(spec.Serialize(), out var parsed));
        Assert.Equal(spec, parsed);
    }

    [Fact]
    public void A_table_wide_spec_round_trips_with_no_partition()
    {
        var spec = new RangeExclusionSpec("ValidFrom", "ValidTo", []);

        Assert.True(RangeExclusionSpec.TryParse(spec.Serialize(), out var parsed));
        Assert.Empty(parsed.PartitionBy);
        Assert.Equal(spec, parsed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a spec")]
    [InlineData("v2|StartsAt|EndsAt||0")] // a future version must not be misread as v1
    [InlineData("v1|StartsAt|EndsAt")]    // truncated
    public void A_value_that_is_not_a_v1_spec_is_rejected(string? value)
    {
        Assert.False(RangeExclusionSpec.TryParse(value, out _));
    }

    [Fact]
    public void The_rule_lands_on_the_entity_type_as_an_annotation()
    {
        var model = BuildModel(builder => builder.Entity<Booking>()
            .HasNonOverlappingRange(x => x.StartsAt, x => x.EndsAt, partitionBy: x => x.RoomId));

        Assert.True(RangeExclusionSpec.TryParse(Annotation(model), out var spec));
        Assert.Equal("StartsAt", spec.Lo);
        Assert.Equal("EndsAt", spec.Hi);
        Assert.Equal(["RoomId"], spec.PartitionBy);
    }

    [Fact]
    public void A_partition_may_name_several_properties()
    {
        var model = BuildModel(builder => builder.Entity<Booking>()
            .HasNonOverlappingRange(x => x.StartsAt, x => x.EndsAt, partitionBy: x => new { x.RoomId, x.Wing }));

        Assert.True(RangeExclusionSpec.TryParse(Annotation(model), out var spec));
        Assert.Equal(["RoomId", "Wing"], spec.PartitionBy);
    }

    [Fact]
    public void A_soft_deletable_entity_frees_its_slot_by_default()
    {
        var model = BuildModel(builder => builder.Entity<Lease>()
            .HasNonOverlappingRange(x => x.StartsAt, x => x.EndsAt));

        Assert.True(RangeExclusionSpec.TryParse(Annotation(model, typeof(Lease)), out var spec));
        Assert.True(spec.IgnoreSoftDeleted);
    }

    [Fact]
    public void A_soft_deletable_entity_can_opt_back_in_to_blocking_the_slot()
    {
        var model = BuildModel(builder => builder.Entity<Lease>()
            .HasNonOverlappingRange(x => x.StartsAt, x => x.EndsAt, ignoreSoftDeleted: false));

        Assert.True(RangeExclusionSpec.TryParse(Annotation(model, typeof(Lease)), out var spec));
        Assert.False(spec.IgnoreSoftDeleted);
    }

    [Fact]
    public void An_entity_that_is_not_soft_deletable_never_gets_the_soft_delete_filter()
    {
        // Asking for it on an entity with no DeletedAt column would emit DDL referencing a column that is
        // not there, so the flag is ignored rather than trusted.
        var model = BuildModel(builder => builder.Entity<Booking>()
            .HasNonOverlappingRange(x => x.StartsAt, x => x.EndsAt, ignoreSoftDeleted: true));

        Assert.True(RangeExclusionSpec.TryParse(Annotation(model), out var spec));
        Assert.False(spec.IgnoreSoftDeleted);
    }

    [Fact]
    public void An_expression_that_is_not_a_plain_property_is_refused()
    {
        var error = Assert.Throws<ArgumentException>(() => BuildModel(builder => builder.Entity<Booking>()
            .HasNonOverlappingRange(x => x.StartsAt + 1, x => x.EndsAt)));

        Assert.Equal("lo", error.ParamName);
    }

    [Fact]
    public void A_partition_expression_must_name_properties_directly()
    {
        var error = Assert.Throws<ArgumentException>(() => BuildModel(builder => builder.Entity<Booking>()
            .HasNonOverlappingRange(x => x.StartsAt, x => x.EndsAt, partitionBy: x => x.Wing!.Length)));

        Assert.Equal("partitionBy", error.ParamName);
    }

    [Fact]
    public void An_entity_that_declares_no_rule_carries_no_annotation()
    {
        var model = BuildModel(builder => builder.Entity<Booking>());

        Assert.Null(Annotation(model));
    }

    private static IModel BuildModel(Action<ModelBuilder> configure)
    {
        var options = new DbContextOptionsBuilder<ProbeContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;

        using var context = new ProbeContext(options, configure);
        return context.Model;
    }

    private static object? Annotation(IModel model, Type? clrType = null)
        => model.FindEntityType(clrType ?? typeof(Booking))!
            .FindAnnotation(RangeExclusionSpec.AnnotationName)?.Value;

    private sealed class Booking
    {
        public int Id { get; set; }

        public int RoomId { get; set; }

        public string? Wing { get; set; }

        public long StartsAt { get; set; }

        public long EndsAt { get; set; }
    }

    private sealed class Lease : ISoftDeletable
    {
        public int Id { get; set; }

        public long StartsAt { get; set; }

        public long EndsAt { get; set; }

        public DateTime? DeletedAt { get; set; }
    }

    // Each test builds a one-off model through the configure delegate. EF caches a built model per (context
    // type, options), so without a unique cache key every test after the first would silently be handed the
    // first test's model.
    private sealed class ProbeContext(DbContextOptions<ProbeContext> options, Action<ModelBuilder> configure)
        : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder) => configure(modelBuilder);

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            => optionsBuilder.ReplaceService<IModelCacheKeyFactory, NeverCache>();
    }

    private sealed class NeverCache : IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime) => Guid.NewGuid();
    }
}
