using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using Rask.Core.Live;
using Rask.Core.Routing;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Components;

public partial class NavLinkTests : global::Rask.Core.RaskMarkup
{
    // A sub-path deploy (#975). The generated Routes.* URL is the route's own path, so the anchor used to
    // come out root-relative: served at /docs/, the link said /guides/x. Clicking it in the app worked —
    // the runtime intercepts the click and routes client-side against its own PathBase — which is exactly
    // why this survived. Everything that is not an intercepted click went to the origin root and 404'd:
    // open in a new tab, middle-click, copy link address, a crawler, any reload.
    //
    // LiveOptions.PathBase is process-wide static, so it is saved and restored. Nothing else in this
    // assembly reads or writes it; if that ever changes, these belong in their own non-parallel
    // collection rather than left to race.
    [Theory]
    [InlineData("", "/users/42")]
    [InlineData("/docs", "/docs/users/42")]
    [InlineData("/docs/", "/docs/users/42")]
    [InlineData("docs", "/docs/users/42")]
    public void The_href_carries_the_deploy_path_base(string pathBase, string expected)
    {
        var prior = LiveOptions.PathBase;
        try
        {
            LiveOptions.PathBase = pathBase;

            Assert.Equal(
                $"<a href=\"{expected}\" data-rask-nav></a>",
                NavLink.Href("/users/42").ToHtml());
        }
        finally
        {
            LiveOptions.PathBase = prior;
        }
    }

    [Fact]
    public void Unset_props_render_only_the_open_and_close_tags_with_the_nav_marker() =>
        Assert.Equal("<a data-rask-nav></a>", NavLink.ToHtml());

    [Fact]
    public void Setting_every_prop_emits_the_expected_attributes()
    {
        Assert.Equal(
            "<a id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" href=\"/users/42\" data-rask-nav></a>",
            NavLink
                .Href("/users/42")
                .Id("i")
                .Class("c")
                .Style("s")
                .Data(new Dictionary<string, string?> { ["k"] = "v" })
                .ToHtml());
    }

    [Fact]
    public void A_string_child_is_encoded_as_text() =>
        Assert.Equal("<a data-rask-nav>&lt;x&gt;</a>", NavLink["<x>"].ToHtml());

    [Fact]
    public void A_RouteUrl_href_renders_the_full_url_with_its_query() => Assert.Equal(
        "<a href=\"/users?id=7\" data-rask-nav></a>", NavLink.Href(new RouteUrl("/users", "?id=7")).ToHtml());

    private static IDisposable BeginRoute(string path, string? rawQuery = null)
    {
        var state = new RouteState { Path = path };
        if (!string.IsNullOrEmpty(rawQuery))
        {
            var dict = new Dictionary<string, StringValues>();
            var qs = rawQuery.StartsWith('?') ? rawQuery[1..] : rawQuery;
            foreach (var part in qs.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var eq = part.IndexOf('=');
                var k = eq < 0 ? part : part[..eq];
                var v = eq < 0 ? string.Empty : part[(eq + 1)..];
                dict[Uri.UnescapeDataString(k)] = Uri.UnescapeDataString(v);
            }

            state.Query = new QueryCollection(dict);
        }

        var services = new ServiceCollection().AddSingleton(state).BuildServiceProvider();
        return LiveRenderContext.Begin(new StubComponent(Span), services);
    }

    [Fact]
    public void A_matching_path_appends_the_active_class()
    {
        using var _ = BeginRoute("/dashboard");

        Assert.Equal(
            "<a class=\"menu-link active\" aria-current=\"page\" href=\"/dashboard\" data-rask-nav></a>",
            NavLink.Href("/dashboard").Class("menu-link").ToHtml());
    }

    [Fact]
    public void A_matching_path_tells_assistive_tech_it_is_the_current_page()
    {
        // A class is invisible to a screen reader; aria-current="page" is what it announces. An empty ActiveClass opts
        // out of the active state altogether, and an aria-current the call site set wins.
        using var _ = BeginRoute("/dashboard");

        Assert.Contains("aria-current=\"page\"", NavLink.Href("/dashboard").ToHtml());
        Assert.DoesNotContain("aria-current", NavLink.Href("/dashboard").ActiveClass("").ToHtml());
        Assert.Contains(
            "aria-current=\"step\"",
            NavLink.Href("/dashboard").Aria("current", "step").ToHtml());
        Assert.DoesNotContain("aria-current", NavLink.Href("/other").ToHtml());
    }

    [Fact]
    public void A_different_path_does_not_append_the_active_class()
    {
        using var _ = BeginRoute("/other");

        Assert.Equal(
            "<a class=\"menu-link\" href=\"/dashboard\" data-rask-nav></a>",
            NavLink.Href("/dashboard").Class("menu-link").ToHtml());
    }

    [Fact]
    public void A_matching_path_with_no_user_class_emits_active_alone()
    {
        using var _ = BeginRoute("/dashboard");

        Assert.Equal(
            "<a class=\"active\" aria-current=\"page\" href=\"/dashboard\" data-rask-nav></a>",
            NavLink.Href("/dashboard").ToHtml());
    }

    [Fact]
    public void A_trailing_slash_still_counts_as_active()
    {
        using var _ = BeginRoute("/dashboard/");

        Assert.Contains("class=\"active\"", NavLink.Href("/dashboard").ToHtml());
    }

    [Fact]
    public void A_path_in_another_case_still_counts_as_active()
    {
        using var _ = BeginRoute("/Dashboard");

        Assert.Contains("class=\"active\"", NavLink.Href("/dashboard").ToHtml());
    }

    [Fact]
    public void An_exact_match_whose_query_is_a_subset_is_active()
    {
        using var _ = BeginRoute("/dashboard", "?tab=billing&extra=1");

        Assert.Contains("class=\"active\"", NavLink.Href(new RouteUrl("/dashboard", "?tab=billing")).ToHtml());
    }

    [Fact]
    public void An_exact_match_missing_the_query_is_not_active()
    {
        using var _ = BeginRoute("/dashboard", "?tab=other");

        Assert.DoesNotContain("active", NavLink.Href(new RouteUrl("/dashboard", "?tab=billing")).ToHtml());
    }

    [Fact]
    public void A_prefix_match_respects_segment_boundaries_so_dash_is_not_dashboard()
    {
        using var _ = BeginRoute("/dashboard");

        Assert.DoesNotContain("active", NavLink.Href("/dash").ActiveMatch(NavLinkMatch.Prefix).ToHtml());
    }

    [Fact]
    public void A_prefix_match_on_a_nested_path_is_active()
    {
        using var _ = BeginRoute("/dashboard/settings");

        Assert.Contains("class=\"active\"", NavLink.Href("/dashboard").ActiveMatch(NavLinkMatch.Prefix).ToHtml());
    }

    [Fact]
    public void A_prefix_match_ignores_the_query()
    {
        using var _ = BeginRoute("/dashboard", "?tab=other");

        Assert.Contains("class=\"active\"",
            NavLink.Href(new RouteUrl("/dashboard", "?tab=billing")).ActiveMatch(NavLinkMatch.Prefix).ToHtml());
    }

    [Fact]
    public void A_custom_active_class_is_used()
    {
        using var _ = BeginRoute("/dashboard");

        Assert.Contains("class=\"nav-pill is-current\"",
            NavLink.Href("/dashboard").Class("nav-pill").ActiveClass("is-current").ToHtml());
    }

    [Fact]
    public void Match_overrides_the_href_for_the_active_check_across_a_section()
    {
        // On a sibling sub-route (/realtime/ETH), a link to /realtime/BTC still lights up because
        // Match points the active comparison at the section root with Prefix matching.
        using var _ = BeginRoute("/realtime/ETH");

        Assert.Equal(
            "<a class=\"active\" aria-current=\"page\" href=\"/realtime/BTC\" data-rask-nav></a>",
            NavLink.Href("/realtime/BTC").Match("/realtime").ActiveMatch(NavLinkMatch.Prefix).ToHtml());
    }

    [Fact]
    public void Match_outside_its_section_is_not_active()
    {
        using var _ = BeginRoute("/other");

        Assert.DoesNotContain("active",
            NavLink.Href("/realtime/BTC").Match("/realtime").ActiveMatch(NavLinkMatch.Prefix).ToHtml());
    }

    [Fact]
    public void Without_a_live_render_context_there_is_no_active_class()
    {
        Assert.Equal(
            "<a class=\"menu-link\" href=\"/dashboard\" data-rask-nav></a>",
            NavLink.Href("/dashboard").Class("menu-link").ToHtml());
    }

    [Fact]
    public void A_live_context_without_route_state_gives_no_active_class()
    {
        var services = RenderHarness.EmptyServices();
        using var _ = LiveRenderContext.Begin(new StubComponent(Span), services);

        Assert.Equal(
            "<a class=\"menu-link\" href=\"/dashboard\" data-rask-nav></a>",
            NavLink.Href("/dashboard").Class("menu-link").ToHtml());
    }
}
