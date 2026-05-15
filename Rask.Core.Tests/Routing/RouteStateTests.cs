using Microsoft.Extensions.Primitives;
using Rask.Core.Routing;

namespace Rask.Core.Tests.Routing;

public class RouteStateTests
{
    [Fact]
    public void Defaults_PathSlash_QueryEmpty()
    {
        var state = new RouteState();

        Assert.Equal("/", state.Path);
        Assert.Same(QueryCollection.Empty, state.Query);
    }

    [Fact]
    public void Mutate_PathAndQuery_RoundTrips()
    {
        var state = new RouteState();
        var q = new QueryCollection(new Dictionary<string, StringValues> { ["x"] = "1" });

        state.Path = "/foo";
        state.Query = q;

        Assert.Equal("/foo", state.Path);
        Assert.Same(q, state.Query);
    }

    [Fact]
    public void SetPath_DifferentValue_RaisesChanged()
    {
        var state = new RouteState();
        var fires = 0;
        state.Changed += () => fires++;

        state.Path = "/foo";

        Assert.Equal(1, fires);
    }

    [Fact]
    public void SetPath_SameValue_DoesNotRaise()
    {
        var state = new RouteState { Path = "/foo" };
        var fires = 0;
        state.Changed += () => fires++;

        state.Path = "/foo";

        Assert.Equal(0, fires);
    }

    [Fact]
    public void SetQuery_DifferentInstance_RaisesChanged()
    {
        var state = new RouteState();
        var fires = 0;
        state.Changed += () => fires++;

        state.Query = new QueryCollection(new Dictionary<string, StringValues> { ["x"] = "1" });

        Assert.Equal(1, fires);
    }

    [Fact]
    public void SetQuery_SameInstance_DoesNotRaise()
    {
        var q = new QueryCollection(new Dictionary<string, StringValues> { ["x"] = "1" });
        var state = new RouteState { Query = q };
        var fires = 0;
        state.Changed += () => fires++;

        state.Query = q;

        Assert.Equal(0, fires);
    }
}
