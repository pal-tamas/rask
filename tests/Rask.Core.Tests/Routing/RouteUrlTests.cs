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

    [Fact]
    public void With_adds_a_key_after_the_ones_the_url_has()
    {
        var list = new RouteUrl("/drivers", "?q=ada&page=2", typeof(RouteUrlTests));

        var editing = list.With("edit", "new");

        Assert.Equal("/drivers?q=ada&page=2&edit=new", editing.ToString());
        Assert.Equal(typeof(RouteUrlTests), editing.PageType);
    }

    [Fact]
    public void With_replaces_a_key_the_url_has_whatever_its_case()
    {
        var list = new RouteUrl("/drivers", "?Edit=3&q=ada");

        var editing = list.With("edit", "7");

        Assert.Equal("/drivers?Edit=7&q=ada", editing.ToString());
    }

    [Fact]
    public void With_no_value_takes_the_key_out_and_the_last_one_leaves_no_query()
    {
        var editing = new RouteUrl("/drivers", "?edit=7");

        var list = editing.With("edit", null);

        Assert.Equal("/drivers", list.ToString());
        Assert.Null(list.QueryString);
    }

    [Fact]
    public void With_escapes_the_value_it_writes()
    {
        var list = new RouteUrl("/drivers");

        var editing = list.With("edit", "a b&c");

        Assert.Equal("/drivers?edit=a%20b%26c", editing.ToString());
    }
}
