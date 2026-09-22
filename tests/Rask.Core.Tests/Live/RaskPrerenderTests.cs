using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Live;
using Rask.Core.Routing;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated chain entries

namespace Rask.Core.Tests.Live;

// Rendering a whole document with no host and no browser — which is what an app with no server needs
// at publish time. A standalone WASM app currently ships its boot shell to every visitor and every
// crawler: a spinner and the word "Loading", because the real markup does not exist until several
// megabytes of runtime have downloaded.
public class RaskPrerenderTests
{
    [Fact]
    public async Task It_renders_a_whole_document()
    {
        var result = await RaskPrerender.RenderDocumentAsync(
            new PlainPage(), Services(), TimeSpan.FromSeconds(5));

        // The shell the hosts compose, not just the component's own markup.
        Assert.Contains("<!doctype html>", result.Html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<body", result.Html, StringComparison.Ordinal);
        Assert.Contains("hello", result.Html, StringComparison.Ordinal);
        Assert.False(result.Faulted);
        Assert.False(result.TimedOut);
    }

    [Fact]
    public async Task It_waits_for_data_the_page_loads_on_mount()
    {
        // The entire reason this goes through the wave loop. Rendering once would write the placeholder
        // — which is exactly the "Loading…" a crawler sees today, in a different costume.
        var result = await RaskPrerender.RenderDocumentAsync(
            new AsyncPage(), Services(), TimeSpan.FromSeconds(5));

        Assert.Contains("loaded", result.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("loading", result.Html, StringComparison.Ordinal);
        Assert.False(result.TimedOut);
    }

    [Fact]
    public async Task A_page_that_throws_is_reported_rather_than_returned_as_if_it_were_the_page()
    {
        // The root boundary catches it and renders a perfectly ordinary error document, so a caller
        // that writes the HTML blindly publishes an error page under the route's own name — and nothing
        // at build time would say so. Faulted is how it says so.
        var result = await RaskPrerender.RenderDocumentAsync(
            new ThrowingPage(), Services(), TimeSpan.FromSeconds(5));

        Assert.True(result.Faulted);
        Assert.NotEmpty(result.Html);
    }

    [Fact]
    public async Task A_faulted_page_says_what_threw()
    {
        // Faulted alone is enough to REFUSE the render and not enough to fix it. A build-time pass has
        // no browser console and no request log, so without the exception a page the pass declined to
        // write is a URL that ships as an empty boot shell for a reason nobody can name — which is
        // exactly how sixteen of this repo's own twenty routes went unwritten while the publish stayed
        // green.
        var result = await RaskPrerender.RenderDocumentAsync(
            new ThrowingPage(), Services(), TimeSpan.FromSeconds(5));

        Assert.NotNull(result.Error);
        Assert.IsType<InvalidOperationException>(result.Error);
        Assert.Equal("boom", result.Error!.Message);
    }

    [Fact]
    public async Task A_page_that_renders_carries_no_error()
    {
        // The other half, so Error cannot become "always populated" and quietly stop meaning anything.
        var result = await RaskPrerender.RenderDocumentAsync(
            new PlainPage(), Services(), TimeSpan.FromSeconds(5));

        Assert.False(result.Faulted);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task Work_that_never_settles_is_reported_rather_than_waited_for_forever()
    {
        // Same trap: the markup that comes back is the placeholder. Baking that is worse than not
        // prerendering the route at all, because it looks prerendered.
        var result = await RaskPrerender.RenderDocumentAsync(
            new NeverSettlesPage(), Services(), TimeSpan.FromMilliseconds(150));

        Assert.True(result.TimedOut);
        Assert.Contains("still-loading", result.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_route_comes_from_the_provider()
    {
        // Which page this renders is the caller's decision: it holds the route table, so it seeds the
        // route. The prerenderer does not guess how routes are enumerated.
        var services = Services();
        services.GetRequiredService<RouteState>().Path = "/seeded";

        var result = await RaskPrerender.RenderDocumentAsync(
            new RouteEchoPage(services.GetRequiredService<RouteState>()), services, TimeSpan.FromSeconds(5));

        Assert.Contains("/seeded", result.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_plan_keeps_routes_whose_every_segment_is_a_literal()
    {
        RouteRegistry.Replace(nameof(The_plan_keeps_routes_whose_every_segment_is_a_literal), [
            new RouteRegistration(typeof(PlainPage), "/", null),
            new RouteRegistration(typeof(PlainPage), "/about", null),
            new RouteRegistration(typeof(PlainPage), "/guides/intro", null),
        ]);

        var plan = RaskPrerender.PlanRoutes();

        Assert.Contains("/", plan.Paths);
        Assert.Contains("/about", plan.Paths);
        Assert.Contains("/guides/intro", plan.Paths);
    }

    [Fact]
    public void The_plan_REPORTS_what_it_cannot_prerender_rather_than_dropping_it()
    {
        // The point of Skipped being a field rather than a log line. A parameterised route cannot be
        // enumerated without knowing the values, and a catch-all is a 404 page at best — but a pass
        // that quietly covered only the static half would read as though it had covered everything.
        RouteRegistry.Replace(nameof(The_plan_REPORTS_what_it_cannot_prerender_rather_than_dropping_it), [
            new RouteRegistration(typeof(PlainPage), "/products/{id}", null),
            new RouteRegistration(typeof(PlainPage), "/docs/{**rest}", null),
            new RouteRegistration(typeof(PlainPage), "/plain", null),
        ]);

        var plan = RaskPrerender.PlanRoutes();

        Assert.Contains("/plain", plan.Paths);
        Assert.DoesNotContain("/products/{id}", plan.Paths);
        Assert.DoesNotContain("/docs/{**rest}", plan.Paths);

        Assert.Contains("/products/{id}", plan.Skipped);
        Assert.Contains("/docs/{**rest}", plan.Skipped);
    }

    private static IServiceProvider Services()
    {
        var services = new ServiceCollection();
        services.AddScoped<RouteState>();
        return services.BuildServiceProvider();
    }

    private sealed class PlainPage : Component
    {
        protected override Component? Render() => Div["hello"];
    }

    private sealed class AsyncPage : Component
    {
        private string? _value;

        protected override async Task OnMount()
        {
            await Task.Delay(20);
            _value = "loaded";
        }

        protected override Component? Render() => Div[_value ?? "loading"];
    }

    private sealed class ThrowingPage : Component
    {
        protected override Component? Render() => throw new InvalidOperationException("boom");
    }

    private sealed class NeverSettlesPage : Component
    {
        protected override Task OnMount() => new TaskCompletionSource().Task;

        protected override Component? Render() => Div["still-loading"];
    }

    private sealed class RouteEchoPage(RouteState route) : Component
    {
        protected override Component? Render() => Div[route.Path];
    }
}
