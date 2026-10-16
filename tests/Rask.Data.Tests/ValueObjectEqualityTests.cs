namespace Rask.Data.Tests;

/// <summary>Not held by any entity: a second value object type with the same components as another.</summary>
public sealed class HarborWeight : ValueObject
{
    public HarborWeight(string street) => Street = street;

    public string Street { get; }

    protected override IEnumerable<object?> EqualityComponents()
    {
        yield return Street;
    }
}

/// <summary>A plain class on <see cref="ValueObject" /> is its values: equal when they are, of the same type.</summary>
public sealed class ValueObjectEqualityTests
{
    [Fact]
    public void Two_value_objects_with_the_same_components_are_equal()
    {
        var first = HarborAddress.Of("Pier 1", null, 4, null, new HarborBerth("A", null));
        var second = HarborAddress.Of("Pier 1", null, 4, null, new HarborBerth("A", null));

        var same = first == second;

        Assert.True(same);
        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void A_different_component_makes_a_different_value()
    {
        var first = HarborAddress.Of("Pier 1", null, 4, null, new HarborBerth("A", null));
        var second = HarborAddress.Of("Pier 1", null, 5, null, new HarborBerth("A", null));

        var same = first == second;

        Assert.False(same);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Value_objects_of_two_types_with_the_same_components_are_different()
    {
        ValueObject weight = new HarborWeight("Pier 1");
        ValueObject other = new OtherWeight("Pier 1");

        var same = weight == other;

        Assert.False(same);
    }

    private sealed class OtherWeight(string street) : ValueObject
    {
        protected override IEnumerable<object?> EqualityComponents()
        {
            yield return street;
        }
    }
}
