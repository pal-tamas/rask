using Rask.Core.Routing;

namespace Rask.Core.Tests.Routing;

public class RouteUrlTests
{
    [Fact]
    public void ToString_PathOnly_ReturnsPath() => Assert.Equal("/users", new RouteUrl("/users").ToString());

    [Fact]
    public void ToString_PathAndQuery_ConcatenatesThem() =>
        Assert.Equal("/users?id=7", new RouteUrl("/users", "?id=7").ToString());

    [Fact]
    public void ToString_EmptyQuery_ReturnsPathOnly() => Assert.Equal("/users", new RouteUrl("/users", "").ToString());

    [Fact]
    public void ImplicitFromString_ProducesPathOnlyRouteUrl()
    {
        RouteUrl url = "/foo";
        Assert.Equal("/foo", url.Path);
        Assert.Null(url.QueryString);
        Assert.Null(url.PageType);
    }

    [Fact]
    public void ImplicitToString_RoundTrips()
    {
        var url = new RouteUrl("/users", "?id=7");
        string s = url;
        Assert.Equal("/users?id=7", s);
    }

    [Fact]
    public void External_ReturnsRouteUrlWithNoPageType()
    {
        var url = RouteUrl.External("https://github.com");
        Assert.Equal("https://github.com", url.Path);
        Assert.Null(url.PageType);
    }

    [Fact]
    public void Equality_SameComponents_AreEqual()
    {
        var a = new RouteUrl("/users", "?id=7", typeof(RouteUrlTests));
        var b = new RouteUrl("/users", "?id=7", typeof(RouteUrlTests));
        Assert.Equal(a, b);
    }

    [Fact]
    public void Equality_DifferentQueryString_AreNotEqual()
    {
        var a = new RouteUrl("/users", "?id=7");
        var b = new RouteUrl("/users", "?id=8");
        Assert.NotEqual(a, b);
    }
}
