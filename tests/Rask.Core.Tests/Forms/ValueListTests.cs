using System.Collections;
using Rask.Values;

namespace Rask.Core.Tests.Forms;

/// <summary>A value list is a value: equal by what it holds, in order, and never changed once read.</summary>
public sealed class ValueListTests
{
    private sealed record Pair(string FileId, int DbId);

    private sealed record Holder(string Name, ValueList<Pair> Pairs);

    [Fact]
    public void Two_lists_holding_the_same_elements_in_order_are_equal_and_hash_alike()
    {
        ValueList<Pair> first = [new("7", 1), new("8", 2)];
        var second = ValueList.Of(new[] { new Pair("7", 1), new Pair("8", 2) }.AsEnumerable());

        var reversed = ValueList.Of([new Pair("8", 2), new Pair("7", 1)]);

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.NotEqual(first, reversed);
    }

    [Fact]
    public void A_record_holding_a_value_list_compares_by_what_the_list_holds()
    {
        var one = new Holder("ta", [new("7", 1)]);

        var other = new Holder("ta", [new("7", 1)]);

        Assert.Equal(one, other);
    }

    [Fact]
    public void A_list_that_has_been_read_refuses_every_change()
    {
        ValueList<string> codes = ["A1", "B2"];

        _ = codes.Count;

        Assert.Throws<NotSupportedException>(() => ((IList<string>)codes).Add("C3"));
        Assert.Throws<NotSupportedException>(() => ((IList)codes).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<string>)codes)[0] = "Z");
        Assert.True(((ICollection<string>)codes).IsReadOnly);
        Assert.Equal("[A1, B2]", codes.ToString());
    }

    [Fact]
    public void A_list_nobody_has_read_is_filled_as_a_reader_fills_it()
    {
        var filled = new ValueList<string>();

        ((ICollection<string>)filled).Add("A1");
        ((IList)filled).Add("B2");

        Assert.Equal(["A1", "B2"], filled);
    }
}
