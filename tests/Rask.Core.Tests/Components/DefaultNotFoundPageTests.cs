using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Live;
using Rask.Core.Routing;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Components;

public partial class DefaultNotFoundPageTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void It_carries_a_page_not_found_heading()
    {
        using var _ = BeginRoute("/missing");

        var html = DefaultNotFoundPage.RenderForLive()!.ToHtml();

        Assert.Contains("Page not found", html);
    }

    [Fact]
    public void It_carries_the_path_that_was_asked_for()
    {
        using var _ = BeginRoute("/does/not/exist");

        var html = DefaultNotFoundPage.RenderForLive()!.ToHtml();

        Assert.Contains("/does/not/exist", html);
    }

    [Fact]
    public void It_links_back_to_the_home_page()
    {
        using var _ = BeginRoute("/anywhere");

        var html = DefaultNotFoundPage.RenderForLive()!.ToHtml();

        Assert.Contains("href=\"/\"", html);
    }

    [Fact]
    public void With_no_route_state_registered_it_falls_back_to_the_root()
    {
        var services = RenderHarness.EmptyServices();
        using var _ = LiveRenderContext.Begin(new StubComponent(Span), services);

        var html = DefaultNotFoundPage.RenderForLive()!.ToHtml();

        Assert.Contains("Page not found", html);
        Assert.Contains(">/<", html);
    }

    [Fact]
    public void It_says_in_one_sentence_that_nothing_is_at_the_path()
    {
        using var _ = BeginRoute("/missing");

        var html = DefaultNotFoundPage.RenderForLive()!.ToHtml();

        Assert.Matches("No route is registered for <code[^>]*>/missing</code>\\.</p>", html);
    }

    [Fact]
    public void A_path_that_carries_markup_is_written_as_text()
    {
        using var _ = BeginRoute("/<img src=x onerror=alert(1)>");

        var html = DefaultNotFoundPage.RenderForLive()!.ToHtml();

        Assert.Contains("/&lt;img src=x onerror=alert(1)&gt;", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<img", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_path_that_reads_like_a_placeholder_is_shown_as_it_was_asked_for()
    {
        using var _ = BeginRoute("/{0}/{1}");

        var html = DefaultNotFoundPage.RenderForLive()!.ToHtml();

        Assert.Matches("<code[^>]*>/\\{0\\}/\\{1\\}</code>", html);
    }

    [Fact]
    public void It_titles_the_tab_over_the_apps_own_title()
    {
        var services = new ServiceCollection().AddSingleton(new RouteState { Path = "/missing" }).BuildServiceProvider();
        var app = new TitledApp();

        var html = new RootErrorBoundary(app).RenderAsLiveRoot(services);

        Assert.Equal("Page not found", Regex.Match(html, "<title[^>]*>([^<]*)<").Groups[1].Value);
        Assert.Single(Regex.Matches(html, "<title"));
    }

    private sealed class TitledApp : Component
    {
        protected override Component? HeadAssets => Title["the app"];

        protected override Component? Render() => Div[DefaultNotFoundPage];
    }

    private static IDisposable BeginRoute(string path)
    {
        var state = new RouteState { Path = path };
        var services = new ServiceCollection().AddSingleton(state).BuildServiceProvider();
        return LiveRenderContext.Begin(new StubComponent(Span), services);
    }
}
