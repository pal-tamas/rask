using Rask.Core;
using Rask.Core.Routing;

namespace Rask.Testing.Playwright.Tests;

public sealed class SitePagesTests
{
    [Fact]
    public void An_apps_pages_are_every_route_it_declares_in_path_order()
    {
        var pages = RaskSite.Pages<Program>();

        Assert.Equal(["/", "/admin", "/login", "/save", "/wide"], pages);
    }

    [Fact]
    public void A_page_under_a_layout_is_listed_at_the_path_the_router_joins()
    {
        Route[] table = [Route.To<Shell>("/admin", [Route.To<Users>("users"), Route.To<Settings>("/settings/")])];

        var pages = SitePages.From(table);

        Assert.Equal(["/admin/settings", "/admin/users"], pages);
    }

    [Fact]
    public void A_page_that_needs_a_value_is_left_out_and_an_optional_one_is_left_empty()
    {
        Route[] table = [Route.To<Users>("/users/{id:int}"), Route.To<Reports>("/reports/{year?}")];

        var pages = SitePages.From(table);

        Assert.Equal(["/reports"], pages);
    }

    [Fact]
    public void A_catch_all_is_no_page_to_walk()
    {
        Route[] table = [Route.To<Users>("/users"), Route.To<Missing>("{**rest}")];

        var pages = SitePages.From(table);

        Assert.Equal(["/users"], pages);
    }

    [Fact]
    public void A_test_adds_a_page_with_its_value_and_leaves_one_out()
    {
        Route[] table = [Route.To<Users>("/users"), Route.To<Users>("/logout"), Route.To<Users>("/users/{id}")];

        var pages = SitePages.From(table).And("/users/7").Without("/logout");

        Assert.Equal(["/users", "/users/7"], pages);
    }

    private sealed class Shell : Component
    {
        protected override Component? Render() => null;
    }

    private sealed class Users : Component
    {
        protected override Component? Render() => null;
    }

    private sealed class Settings : Component
    {
        protected override Component? Render() => null;
    }

    private sealed class Reports : Component
    {
        protected override Component? Render() => null;
    }

    private sealed class Missing : Component
    {
        protected override Component? Render() => null;
    }
}
