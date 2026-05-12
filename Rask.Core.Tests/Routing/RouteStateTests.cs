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
}
