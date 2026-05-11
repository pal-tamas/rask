using Rask.Core.Components;
using Rask.Core.Live;
using Rask.Core.Routing;
using Rask.Core.Tests.Live;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;

namespace Rask.Core.Tests.Components;

public class NavLinkTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags_WithDataRaskNav() =>
        Assert.Equal("<a data-rask-nav></a>", new NavLink(null).ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        var props = new NavLink.Props(
            "/users/42",
            "i",
            "c",
            "s",
            new Dictionary<string, string?> { ["k"] = "v" });

        Assert.Equal(
            "<a id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" href=\"/users/42\" data-rask-nav></a>",
            new NavLink(props).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<a data-rask-nav>&lt;x&gt;</a>", new NavLink(null, "<x>").ToHtml());

    [Fact]
    public void Render_HrefRouteUrl_RendersFullUrlWithQuery()
    {
        var props = new NavLink.Props(new RouteUrl("/users", "?id=7"));
        Assert.Equal("<a href=\"/users?id=7\" data-rask-nav></a>", new NavLink(props).ToHtml());
    }

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
        return LiveRenderContext.Begin(new StubComponent(new Span(null)), services);
    }

    [Fact]
    public void Render_PathsMatch_AppendsActiveClass()
    {
        using var _ = BeginRoute("/dashboard");
        var props = new NavLink.Props("/dashboard", Class: "nav-link");
        Assert.Equal(
            "<a class=\"nav-link active\" href=\"/dashboard\" data-rask-nav></a>",
            new NavLink(props).ToHtml());
    }

    [Fact]
    public void Render_PathsDiffer_DoesNotAppendActive()
    {
        using var _ = BeginRoute("/other");
        var props = new NavLink.Props("/dashboard", Class: "nav-link");
        Assert.Equal(
            "<a class=\"nav-link\" href=\"/dashboard\" data-rask-nav></a>",
            new NavLink(props).ToHtml());
    }

    [Fact]
    public void Render_PathsMatch_NoUserClass_EmitsActiveAlone()
    {
        using var _ = BeginRoute("/dashboard");
        var props = new NavLink.Props("/dashboard");
        Assert.Equal(
            "<a class=\"active\" href=\"/dashboard\" data-rask-nav></a>",
            new NavLink(props).ToHtml());
    }

    [Fact]
    public void Render_TrailingSlashEquivalence_StillActive()
    {
        using var _ = BeginRoute("/dashboard/");
        var props = new NavLink.Props("/dashboard");
        Assert.Contains("class=\"active\"", new NavLink(props).ToHtml());
    }

    [Fact]
    public void Render_CaseInsensitivePath_StillActive()
    {
        using var _ = BeginRoute("/Dashboard");
        var props = new NavLink.Props("/dashboard");
        Assert.Contains("class=\"active\"", new NavLink(props).ToHtml());
    }

    [Fact]
    public void Render_ExactMatch_QuerySubset_IsActive()
    {
        using var _ = BeginRoute("/dashboard", "?tab=billing&extra=1");
        var props = new NavLink.Props(new RouteUrl("/dashboard", "?tab=billing"));
        Assert.Contains("class=\"active\"", new NavLink(props).ToHtml());
    }

    [Fact]
    public void Render_ExactMatch_QueryMissing_NotActive()
    {
        using var _ = BeginRoute("/dashboard", "?tab=other");
        var props = new NavLink.Props(new RouteUrl("/dashboard", "?tab=billing"));
        Assert.DoesNotContain("active", new NavLink(props).ToHtml());
    }

    [Fact]
    public void Render_PrefixMatch_BoundaryAware_DashIsNotDashboard()
    {
        using var _ = BeginRoute("/dashboard");
        var props = new NavLink.Props(
            "/dash",
            ActiveMatch: NavLinkMatch.Prefix);
        Assert.DoesNotContain("active", new NavLink(props).ToHtml());
    }

    [Fact]
    public void Render_PrefixMatch_NestedPath_IsActive()
    {
        using var _ = BeginRoute("/dashboard/settings");
        var props = new NavLink.Props(
            "/dashboard",
            ActiveMatch: NavLinkMatch.Prefix);
        Assert.Contains("class=\"active\"", new NavLink(props).ToHtml());
    }

    [Fact]
    public void Render_PrefixMatch_IgnoresQuery()
    {
        using var _ = BeginRoute("/dashboard", "?tab=other");
        var props = new NavLink.Props(
            new RouteUrl("/dashboard", "?tab=billing"),
            ActiveMatch: NavLinkMatch.Prefix);
        Assert.Contains("class=\"active\"", new NavLink(props).ToHtml());
    }

    [Fact]
    public void Render_CustomActiveClass_Used()
    {
        using var _ = BeginRoute("/dashboard");
        var props = new NavLink.Props(
            "/dashboard",
            Class: "nav-pill",
            ActiveClass: "is-current");
        Assert.Contains("class=\"nav-pill is-current\"", new NavLink(props).ToHtml());
    }

    [Fact]
    public void Render_NoLiveRenderContext_NoActiveClass()
    {
        var props = new NavLink.Props("/dashboard", Class: "nav-link");
        Assert.Equal(
            "<a class=\"nav-link\" href=\"/dashboard\" data-rask-nav></a>",
            new NavLink(props).ToHtml());
    }

    [Fact]
    public void Render_LiveRenderContext_NoRouteStateRegistered_NoActiveClass()
    {
        var services = new ServiceCollection().BuildServiceProvider();
        using var _ = LiveRenderContext.Begin(new StubComponent(new Span(null)), services);

        var props = new NavLink.Props("/dashboard", Class: "nav-link");
        Assert.Equal(
            "<a class=\"nav-link\" href=\"/dashboard\" data-rask-nav></a>",
            new NavLink(props).ToHtml());
    }
}
