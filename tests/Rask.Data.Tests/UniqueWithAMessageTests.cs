using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Rask.Data.Tests;

/// <summary>Not an entity anybody maps: only something to declare indexes on.</summary>
public sealed class Shelf
{
    public int Id { get; set; }

    public string Label { get; set; } = "";

    public string Aisle { get; set; } = "";

    public string Barcode { get; set; } = "";

    public string Zone { get; set; } = "";
}

/// <summary>
/// <c>IsUnique("message")</c> is one step for "unique, and this is what breaking it means". It sits beside EF
/// Core's own <c>IsUnique(bool unique = true)</c>, so which call binds to which is pinned here: a string is
/// Rask's, and nothing or a bool is still EF Core's.
/// </summary>
public sealed class UniqueWithAMessageTests
{
    private const string Taken = "That label is already on a shelf.";

    [Fact]
    public void A_string_makes_the_index_unique_and_records_the_message()
    {
        var shelf = new ModelBuilder().Entity<Shelf>();

        IndexBuilder<Shelf> index = shelf.HasIndex(s => s.Label).IsUnique(Taken);

        Assert.True(index.Metadata.IsUnique);
        Assert.Equal(Taken, MessageOf(index.Metadata));
    }

    [Fact]
    public void No_argument_is_still_ef_cores_and_records_no_message()
    {
        var shelf = new ModelBuilder().Entity<Shelf>();

        var index = shelf.HasIndex(s => s.Aisle).IsUnique();

        Assert.True(index.Metadata.IsUnique);
        Assert.Null(MessageOf(index.Metadata));
    }

    [Fact]
    public void A_bool_is_still_ef_cores_and_false_leaves_the_index_plain()
    {
        var shelf = new ModelBuilder().Entity<Shelf>();

        var index = shelf.HasIndex(s => s.Barcode).IsUnique(false);

        Assert.False(index.Metadata.IsUnique);
        Assert.Null(MessageOf(index.Metadata));
    }

    [Fact]
    public void The_builder_that_is_not_generic_takes_the_message_the_same_way()
    {
        var shelf = new ModelBuilder().Entity(typeof(Shelf));
        shelf.Property<string>(nameof(Shelf.Zone));

        IndexBuilder index = shelf.HasIndex(nameof(Shelf.Zone)).IsUnique(Taken);

        Assert.True(index.Metadata.IsUnique);
        Assert.Equal(Taken, MessageOf(index.Metadata));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_message_that_says_nothing_is_refused_and_the_index_is_left_as_it_was(string? message)
    {
        var index = new ModelBuilder().Entity<Shelf>().HasIndex(s => s.Label);

        var refused = Assert.ThrowsAny<ArgumentException>(() => index.IsUnique(message!));

        Assert.Equal("message", refused.ParamName);
        Assert.False(index.Metadata.IsUnique);
    }

    private static object? MessageOf(IReadOnlyIndex index) => index.FindAnnotation(UniqueViolation.Annotation)?.Value;
}
