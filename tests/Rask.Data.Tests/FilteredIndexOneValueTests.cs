using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Rask.Data.Tests;

/// <summary>A numbered slip, whose number is a one-value value object stored in a column of its own.</summary>
public sealed class NumberedSlip
{
    public static readonly RigRank First = RigRank.Create(1, nameof(First));

    public Guid Id { get; set; }

    public RigRank Number { get; set; } = null!;

    public RigRank? Previous { get; set; }

    public Guid? ParentId { get; set; }

    public int Twice { get; private set; }
}

/// <summary>An index's <c>Where(…)</c> over a one-value value object: its <c>.Value</c>, or the object itself, is the column.</summary>
[Collection(DataDbCollection.Name)]
public sealed class FilteredIndexOneValueTests
{
    [Fact]
    public void A_one_value_value_object_s_Value_is_its_column_compared_with_a_plain_number()
    {
        using var context = RefusedContext.Declaring(m =>
            Slip(m).HasIndex(s => s.ParentId).Where(s => s.ParentId == null && s.Number.Value > 0));

        var filter = Filter(context);

        Assert.Equal("\"ParentId\" IS NULL AND \"Number\" > 0", filter);
    }

    [Fact]
    public void A_one_value_value_object_compared_with_one_of_its_own_is_written_as_its_column_stores_it()
    {
        using var context = RefusedContext.Declaring(m =>
            Slip(m).HasIndex(s => s.ParentId).Where(s => s.Number != NumberedSlip.First));

        var filter = Filter(context);

        Assert.Equal("\"Number\" <> 1", filter);
    }

    [Fact]
    public void An_optional_one_value_value_object_s_Value_keeps_the_nulls_out_as_C_sharp_would_throw_on_them()
    {
        using var context = RefusedContext.Declaring(m =>
            Slip(m).HasIndex(s => s.ParentId).Where(s => s.Previous != null && s.Previous.Value >= 2));

        var filter = Filter(context);

        Assert.Equal("\"Previous\" IS NOT NULL AND \"Previous\" >= 2", filter);
    }

    [Fact]
    public void A_computed_column_reads_a_one_value_value_object_s_Value_as_its_column()
    {
        using var context = RefusedContext.Declaring(m =>
            Slip(m).Computed(s => s.Twice, s => s.Number.Value * 2));

        var computed = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(NumberedSlip))!.FindProperty(nameof(NumberedSlip.Twice))!.GetComputedColumnSql();

        Assert.Equal("(\"Number\" * 2)", computed);
    }

    // The columns Rask's model registry gives an aggregate's one-value value objects, given here by hand.
    private static EntityTypeBuilder<NumberedSlip> Slip(ModelBuilder modelBuilder)
    {
        var slip = modelBuilder.Entity<NumberedSlip>();
        ValueObjectColumn.Map(slip.Property(s => s.Number), (RigRank v) => v.Value);
        ValueObjectColumn.Map(slip.Property(s => s.Previous), (RigRank v) => v.Value);
        return slip;
    }

    private static string? Filter(DbContext context) =>
        context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(NumberedSlip))!.GetIndexes().Single(i => i.GetFilter() is not null).GetFilter();
}
