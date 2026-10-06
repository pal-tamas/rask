using Rask.Core.Routing;

namespace Rask.Server.Tests.Routing;

public sealed class SessionRouteMemoTests
{
    private static readonly IReadOnlyList<Route> Table =
    [
        new Route(typeof(Shell), "/app", [new Route(typeof(Orders), "orders")]),
        new Route(typeof(Login), "/login")
    ];

    [Fact]
    public void A_path_asked_for_twice_resolves_to_the_same_pages_both_times()
    {
        var memo = new SessionRouteMemo();

        var first = memo.TryResolve(Table, "/app/orders", out var firstChain);
        var second = memo.TryResolve(Table, "/app/orders", out var secondChain);

        Assert.True(first);
        Assert.True(second);
        Assert.Equal([typeof(Shell), typeof(Orders)], firstChain);
        Assert.Equal(firstChain, secondChain);
    }

    [Fact]
    public void A_session_that_moved_to_another_path_resolves_the_new_one()
    {
        var memo = new SessionRouteMemo();
        memo.TryResolve(Table, "/app/orders", out _);

        var matched = memo.TryResolve(Table, "/login", out var chain);

        Assert.True(matched);
        Assert.Equal([typeof(Login)], chain);
    }

    [Fact]
    public void A_path_no_route_answers_stays_unmatched_when_asked_again()
    {
        var memo = new SessionRouteMemo();
        memo.TryResolve(Table, "/nowhere", out _);

        var matched = memo.TryResolve(Table, "/nowhere", out var chain);

        Assert.False(matched);
        Assert.Empty(chain);
    }

    [Fact]
    public void A_replaced_route_table_is_resolved_afresh_for_the_same_path()
    {
        var memo = new SessionRouteMemo();
        memo.TryResolve(Table, "/login", out _);
        IReadOnlyList<Route> reloaded = [new Route(typeof(Orders), "/login")];

        var matched = memo.TryResolve(reloaded, "/login", out var chain);

        Assert.True(matched);
        Assert.Equal([typeof(Orders)], chain);
    }

    private sealed class Shell;

    private sealed class Orders;

    private sealed class Login;
}
