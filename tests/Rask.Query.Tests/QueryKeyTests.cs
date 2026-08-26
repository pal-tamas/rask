namespace Rask.Query.Tests;

/// <summary>
///     What a query key is, and what matches it.
/// </summary>
/// <remarks>
///     These pin the decisions rather than the mechanics: which order matters and which does not, what a
///     prefix reaches, and what a subset means. Each one is a rule TanStack Query also keeps, and the
///     value of keeping it is that anyone who knows that library already knows this one.
/// </remarks>
public class QueryKeyTests
{
    [Fact]
    public void The_order_of_the_parts_is_part_of_the_key()
    {
        // ["orders", "list"] is not ["list", "orders"]. This is the half of the rule that IS ordered,
        // and it is what makes a prefix mean anything at all.
        Assert.NotEqual(QueryKey.Of("orders", "list"), QueryKey.Of("list", "orders"));
    }

    [Fact]
    public void The_order_of_the_fields_inside_a_part_is_not()
    {
        // …and this is the half that is NOT. Two components writing the same filter in a different order
        // must share one entry and one request, or the cache silently doubles.
        Assert.Equal(
            QueryKey.Of("orders", QueryKey.Fields(("page", 1), ("status", "done"))),
            QueryKey.Of("orders", QueryKey.Fields(("status", "done"), ("page", 1))));

        Assert.Equal(
            QueryKey.Of("orders", QueryKey.Fields(("page", 1), ("status", "done"))).GetHashCode(),
            QueryKey.Of("orders", QueryKey.Fields(("status", "done"), ("page", 1))).GetHashCode());
    }

    [Fact]
    public void A_prefix_matches_everything_beneath_it()
    {
        var key = QueryKey.Of("orders", "list", QueryKey.Fields(("page", 1)));

        Assert.True(key.Matches(QueryKey.Of("orders")));
        Assert.True(key.Matches(QueryKey.Of("orders", "list")));
        Assert.True(key.Matches(key));
    }

    [Fact]
    public void A_different_prefix_matches_nothing()
    {
        var key = QueryKey.Of("orders", "list");

        Assert.False(key.Matches(QueryKey.Of("customers")));
        Assert.False(key.Matches(QueryKey.Of("orders", "detail")));
    }

    [Fact]
    public void A_filter_longer_than_the_key_never_matches()
    {
        // The direction matters: ["orders"] is matched BY ["orders", "list"], not the other way round.
        // Getting this backwards would make invalidating one detail clear the whole list.
        Assert.False(QueryKey.Of("orders").Matches(QueryKey.Of("orders", "list")));
    }

    [Fact]
    public void Fields_are_matched_as_a_subset()
    {
        var key = QueryKey.Of("orders", QueryKey.Fields(("page", 1), ("status", "done")));

        // "every done order, whatever the page" — expressible only because this is a subset test rather
        // than an equality one.
        Assert.True(key.Matches(QueryKey.Of("orders", QueryKey.Fields(("status", "done")))));
        Assert.False(key.Matches(QueryKey.Of("orders", QueryKey.Fields(("status", "draft")))));
        Assert.False(key.Matches(QueryKey.Of("orders", QueryKey.Fields(("archived", true)))));
    }

    [Fact]
    public void A_type_part_and_a_string_part_are_different_keys()
    {
        // What lets derived and hand-written keys share one cache. A message derives
        // [typeof(GetOrders), …]; a hand-written key starting "GetOrders" must not collide with it, or
        // invalidating one would silently clear the other.
        Assert.False(QueryKey.Of(typeof(GetOrders), new GetOrders(1)).Matches(QueryKey.Of("GetOrders")));
        Assert.NotEqual(QueryKey.Of(typeof(GetOrders)), QueryKey.Of("GetOrders"));
    }

    [Fact]
    public void A_message_keys_itself_by_its_type_and_its_value()
    {
        // Records give structural equality for free, which is the whole reason the message can BE the
        // key: the same query written in two components is one entry.
        Assert.Equal(
            QueryKey.Of(typeof(GetOrders), new GetOrders(1)),
            QueryKey.Of(typeof(GetOrders), new GetOrders(1)));

        Assert.NotEqual(
            QueryKey.Of(typeof(GetOrders), new GetOrders(1)),
            QueryKey.Of(typeof(GetOrders), new GetOrders(2)));
    }

    [Fact]
    public void An_empty_key_is_refused()
    {
        // It would match every entry in the cache. If that is what you meant, InvalidateAll says so.
        Assert.Throws<ArgumentException>(() => QueryKey.Of());
    }

    [Fact]
    public void A_key_reads_as_what_it_is()
    {
        // It ends up in a debugger watch window and a failed assertion message, and an unreadable one
        // makes both useless.
        Assert.Equal(
            "['orders', 'list', { page: 1 }]",
            QueryKey.Of("orders", "list", QueryKey.Fields(("page", 1))).ToString());
    }
}
