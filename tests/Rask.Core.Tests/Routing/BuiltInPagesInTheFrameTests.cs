using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Components;
using Rask.Core.Live;
using Rask.Core.Routing;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Routing;

// The built-in 404 and 403 stand inside the app's frame: the 404 under the root layout its pages nest under, the 403
// in the outlet of the page it refuses. With the kit on, the host hands Core the kit's look for both.
[Collection("RouteRegistry")]
public sealed partial class BuiltInPagesInTheFrameTests : global::Rask.Core.RaskMarkup, IDisposable
{
    public BuiltInPagesInTheFrameTests() => RouteRegistry.Reset();

    public void Dispose() => RouteRegistry.Reset();

    [Fact]
    public void The_not_found_page_renders_inside_the_root_layout()
    {
        RegisterTheApp();

        var html = RenderAt("/no/such/page");

        Assert.Matches("^<div id=\"frame\"><nav>menu</nav><main>.*Page not found.*</main></div>$", html);
    }

    [Fact]
    public void The_not_found_page_inside_the_root_layout_still_answers_not_found()
    {
        RegisterTheApp();

        var matched = RouteResolver.TryResolve("/no/such/page", out var chain, out var isNotFound);

        Assert.True(matched);
        Assert.True(isNotFound);
        Assert.Equal([typeof(Frame), typeof(DefaultNotFoundPage)], chain);
    }

    [Fact]
    public void An_apps_own_not_found_page_still_wins()
    {
        RegisterTheApp();
        RouteRegistry.Add([new RouteRegistration(typeof(OwnNotFound), RouteRegistry.DefaultFallbackTemplate, typeof(Frame))]);

        var html = RenderAt("/no/such/page");

        Assert.Equal("<div id=\"frame\"><nav>menu</nav><main><p>own 404</p></main></div>", html);
    }

    [Fact]
    public void With_the_kit_on_the_not_found_page_is_the_kits_inside_the_frame()
    {
        RegisterTheApp();

        var html = RenderAt("/no/such/page", Kit());

        Assert.Equal("<div id=\"frame\"><nav>menu</nav><main><p>kit 404</p></main></div>", html);
    }

    [Fact]
    public void With_the_kit_on_a_forbidden_page_is_the_kits_inside_the_frame()
    {
        RegisterTheApp();

        var html = RenderAt("/about", Kit(), forbidden: typeof(About));

        Assert.Equal("<div id=\"frame\"><nav>menu</nav><main><p>kit 403</p></main></div>", html);
    }

    [Fact]
    public void Without_the_kit_a_forbidden_page_is_rasks_own_sentence()
    {
        RegisterTheApp();

        var html = RenderAt("/about", forbidden: typeof(About));

        Assert.Equal(
            "<div id=\"frame\"><nav>menu</nav><main><p class=\"rask-forbidden\" role=\"alert\">You do not have permission to view this page.</p></main></div>",
            html);
    }

    private static void RegisterTheApp()
    {
        RouteRegistry.SetDefaultFallback(typeof(DefaultNotFoundPage));
        RouteRegistry.Add(
        [
            new RouteRegistration(typeof(Frame), "/", null),
            new RouteRegistration(typeof(About), "about", typeof(Frame)),
        ]);
    }

    private static RaskDocumentDefaults Kit() =>
        new(null, null, null, notFound: () => P["kit 404"], forbidden: () => P["kit 403"]);

    private static string RenderAt(string path, RaskDocumentDefaults? kit = null, Type? forbidden = null)
    {
        var services = new ServiceCollection().AddSingleton(new RouteState { Path = path, Forbidden = forbidden });
        if (kit is not null)
        {
            services.AddSingleton(kit);
        }

        return new StubComponent(() => Router).RenderAsLiveRoot(services.BuildServiceProvider());
    }

    [SkipFactory]
    public sealed class Frame : Component
    {
        protected override Component? Render() => Div.Id("frame")[Nav["menu"], Main[Outlet]];
    }

    [SkipFactory]
    public sealed class About : Component
    {
        protected override Component? Render() => P["about"];
    }

    [SkipFactory]
    public sealed class OwnNotFound : Component
    {
        protected override Component? Render() => P["own 404"];
    }
}
