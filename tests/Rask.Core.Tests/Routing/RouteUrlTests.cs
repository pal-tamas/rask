using Rask.Core.Routing;

namespace Rask.Core.Tests.Routing;

public class RouteUrlTests
{
    [Fact]
    public void A_path_only_url_prints_as_its_path() => Assert.Equal("/users", new RouteUrl("/users").ToString());

    [Fact]
    public void A_path_and_query_print_concatenated() =>
        Assert.Equal("/users?id=7", new RouteUrl("/users", "?id=7").ToString());

    [Fact]
    public void An_empty_query_prints_as_the_path_only() => Assert.Equal("/users", new RouteUrl("/users", "").ToString());

    [Fact]
    public void A_string_converts_implicitly_to_a_path_only_RouteUrl()
    {
        RouteUrl url = "/foo";

        Assert.Equal("/foo", url.Path);
        Assert.Null(url.QueryString);
        Assert.Null(url.PageType);
    }

    [Fact]
    public void A_RouteUrl_converts_implicitly_back_to_its_string()
    {
        var url = new RouteUrl("/users", "?id=7");
        string s = url;

        Assert.Equal("/users?id=7", s);
    }

    [Fact]
    public void An_external_url_has_no_page_type()
    {
        var url = RouteUrl.External("https://github.com");

        Assert.Equal("https://github.com", url.Path);
        Assert.Null(url.PageType);
    }

    [Fact]
    public void Urls_with_the_same_parts_are_equal()
    {
        var a = new RouteUrl("/users", "?id=7", typeof(RouteUrlTests));
        var b = new RouteUrl("/users", "?id=7", typeof(RouteUrlTests));

        Assert.Equal(a, b);
    }

    [Fact]
    public void Urls_with_different_query_strings_are_not_equal()
    {
        var a = new RouteUrl("/users", "?id=7");
        var b = new RouteUrl("/users", "?id=8");

        Assert.NotEqual(a, b);
    }
}
