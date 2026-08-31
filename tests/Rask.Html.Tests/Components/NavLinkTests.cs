using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using Rask.Core.Live;
using Rask.Core.Routing;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Html.Tests.Components;

public partial class NavLinkTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags_WithDataRaskNav() =>
        Assert.Equal("<a data-rask-nav></a>", NavLink.ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
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
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<a data-rask-nav>&lt;x&gt;</a>", NavLink["<x>"].ToHtml());

    [Fact]
    public void Render_HrefRouteUrl_RendersFullUrlWithQuery() => Assert.Equal(
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
    public void Render_PathsMatch_AppendsActiveClass()
    {
        using var _ = BeginRoute("/dashboard");
        Assert.Equal(
            "<a class=\"menu-link active\" href=\"/dashboard\" data-rask-nav></a>",
            NavLink.Href("/dashboard").Class("menu-link").ToHtml());
    }

    [Fact]
    public void Render_PathsDiffer_DoesNotAppendActive()
    {
        using var _ = BeginRoute("/other");
        Assert.Equal(
            "<a class=\"menu-link\" href=\"/dashboard\" data-rask-nav></a>",
            NavLink.Href("/dashboard").Class("menu-link").ToHtml());
    }

    [Fact]
    public void Render_PathsMatch_NoUserClass_EmitsActiveAlone()
    {
        using var _ = BeginRoute("/dashboard");
        Assert.Equal(
            "<a class=\"active\" href=\"/dashboard\" data-rask-nav></a>",
            NavLink.Href("/dashboard").ToHtml());
    }

    [Fact]
    public void Render_TrailingSlashEquivalence_StillActive()
    {
        using var _ = BeginRoute("/dashboard/");
        Assert.Contains("class=\"active\"", NavLink.Href("/dashboard").ToHtml());
    }

    [Fact]
    public void Render_CaseInsensitivePath_StillActive()
    {
        using var _ = BeginRoute("/Dashboard");
        Assert.Contains("class=\"active\"", NavLink.Href("/dashboard").ToHtml());
    }

    [Fact]
    public void Render_ExactMatch_QuerySubset_IsActive()
    {
        using var _ = BeginRoute("/dashboard", "?tab=billing&extra=1");
        Assert.Contains("class=\"active\"", NavLink.Href(new RouteUrl("/dashboard", "?tab=billing")).ToHtml());
    }

    [Fact]
    public void Render_ExactMatch_QueryMissing_NotActive()
    {
        using var _ = BeginRoute("/dashboard", "?tab=other");
        Assert.DoesNotContain("active", NavLink.Href(new RouteUrl("/dashboard", "?tab=billing")).ToHtml());
    }

    [Fact]
    public void Render_PrefixMatch_BoundaryAware_DashIsNotDashboard()
    {
        using var _ = BeginRoute("/dashboard");
        Assert.DoesNotContain("active", NavLink.Href("/dash").ActiveMatch(NavLinkMatch.Prefix).ToHtml());
    }

    [Fact]
    public void Render_PrefixMatch_NestedPath_IsActive()
    {
        using var _ = BeginRoute("/dashboard/settings");
        Assert.Contains("class=\"active\"", NavLink.Href("/dashboard").ActiveMatch(NavLinkMatch.Prefix).ToHtml());
    }

    [Fact]
    public void Render_PrefixMatch_IgnoresQuery()
    {
        using var _ = BeginRoute("/dashboard", "?tab=other");
        Assert.Contains("class=\"active\"",
            NavLink.Href(new RouteUrl("/dashboard", "?tab=billing")).ActiveMatch(NavLinkMatch.Prefix).ToHtml());
    }

    [Fact]
    public void Render_CustomActiveClass_Used()
    {
        using var _ = BeginRoute("/dashboard");
        Assert.Contains("class=\"nav-pill is-current\"",
            NavLink.Href("/dashboard").Class("nav-pill").ActiveClass("is-current").ToHtml());
    }

    [Fact]
    public void Render_Match_OverridesHrefForActive_AcrossSection()
    {
        // On a sibling sub-route (/realtime/ETH), a link to /realtime/BTC still lights up because
        // Match points the active comparison at the section root with Prefix matching.
        using var _ = BeginRoute("/realtime/ETH");
        Assert.Equal(
            "<a class=\"active\" href=\"/realtime/BTC\" data-rask-nav></a>",
            NavLink.Href("/realtime/BTC").Match("/realtime").ActiveMatch(NavLinkMatch.Prefix).ToHtml());
    }

    [Fact]
    public void Render_Match_OutsideSection_NotActive()
    {
        using var _ = BeginRoute("/other");
        Assert.DoesNotContain("active",
            NavLink.Href("/realtime/BTC").Match("/realtime").ActiveMatch(NavLinkMatch.Prefix).ToHtml());
    }

    [Fact]
    public void Render_NoLiveRenderContext_NoActiveClass()
    {
        Assert.Equal(
            "<a class=\"menu-link\" href=\"/dashboard\" data-rask-nav></a>",
            NavLink.Href("/dashboard").Class("menu-link").ToHtml());
    }

    [Fact]
    public void Render_LiveRenderContext_NoRouteStateRegistered_NoActiveClass()
    {
        var services = RenderHarness.EmptyServices();
        using var _ = LiveRenderContext.Begin(new StubComponent(Span), services);

        Assert.Equal(
            "<a class=\"menu-link\" href=\"/dashboard\" data-rask-nav></a>",
            NavLink.Href("/dashboard").Class("menu-link").ToHtml());
    }
}
