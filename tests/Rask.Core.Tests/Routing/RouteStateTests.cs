using Microsoft.Extensions.Primitives;
using Rask.Core.Routing;

namespace Rask.Core.Tests.Routing;

public class RouteStateTests
{
    [Fact]
    public void The_defaults_are_a_slash_path_and_an_empty_query()
    {
        var state = new RouteState();

        Assert.Equal("/", state.Path);
        Assert.Same(QueryCollection.Empty, state.Query);
    }

    [Fact]
    public void A_set_path_and_query_round_trip()
    {
        var state = new RouteState();
        var q = new QueryCollection(new Dictionary<string, StringValues> { ["x"] = "1" });

        state.Path = "/foo";
        state.Query = q;

        Assert.Equal("/foo", state.Path);
        Assert.Same(q, state.Query);
    }

    [Fact]
    public void Setting_a_different_path_raises_Changed()
    {
        var state = new RouteState();
        var fires = 0;
        state.Changed += () => fires++;

        state.Path = "/foo";

        Assert.Equal(1, fires);
    }

    [Fact]
    public void Setting_the_same_path_does_not_raise_Changed()
    {
        var state = new RouteState { Path = "/foo" };
        var fires = 0;
        state.Changed += () => fires++;

        state.Path = "/foo";

        Assert.Equal(0, fires);
    }

    [Fact]
    public void Setting_a_different_query_instance_raises_Changed()
    {
        var state = new RouteState();
        var fires = 0;
        state.Changed += () => fires++;

        state.Query = new QueryCollection(new Dictionary<string, StringValues> { ["x"] = "1" });

        Assert.Equal(1, fires);
    }

    [Fact]
    public void Setting_the_same_query_instance_does_not_raise_Changed()
    {
        var q = new QueryCollection(new Dictionary<string, StringValues> { ["x"] = "1" });
        var state = new RouteState { Query = q };
        var fires = 0;
        state.Changed += () => fires++;

        state.Query = q;

        Assert.Equal(0, fires);
    }
}
