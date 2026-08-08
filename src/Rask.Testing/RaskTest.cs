using Rask.Core;
using Rask.Core.Forms;
using Rask.Core.Live;

namespace Rask.Testing;

/// <summary>
///     Entry point for unit-testing Rask components. <see cref="Render{T}(T, IServiceProvider)" />
///     renders a component to HTML with its live event handlers wired, and returns a
///     <see cref="RenderedComponent" /> you can query and drive (invoke handlers, re-render) — no browser,
///     server, or WebSocket involved. Pass a factory (<see cref="Render(Func{Component}, IServiceProvider)" />)
///     instead when a re-render should rebuild the tree from your current state.
/// </summary>
public static class RaskTest
{
    /// <summary>
    ///     Renders <paramref name="component" /> as a live root and returns a handle to the result. The
    ///     component is wrapped in a forwarding root (so any component — including one that can't be a
    ///     page root — works), its event handlers are registered, and <see cref="RenderedComponent.Html" />
    ///     holds the initial markup. The handle's <see cref="RenderedComponent{T}.Instance" /> is this same
    ///     object, so a test can assert against the component's own state as well as its markup.
    /// </summary>
    /// <typeparam name="T">The component's type, inferred from <paramref name="component" />.</typeparam>
    /// <param name="component">The component under test.</param>
    /// <param name="services">
    ///     Services available to the component (constructor-injected framework services, your own
    ///     registrations). Defaults to an empty provider.
    /// </param>
    public static RenderedComponent<T> Render<T>(T component, IServiceProvider? services = null)
        where T : Component
    {
        ArgumentNullException.ThrowIfNull(component);
        return new RenderedComponent<T>(new TestRoot(() => component), component, services ?? EmptyServices);
    }

    /// <summary>
    ///     Renders the component produced by <paramref name="factory" /> as a live root and returns a handle
    ///     to the result. The factory runs on <b>every</b> render, so the tree is rebuilt from your current
    ///     state each time — use this (rather than the <see cref="Render{T}(T, IServiceProvider)" />
    ///     overload, which renders one fixed instance) whenever a re-render should see changed props:
    ///     <c>RaskTest.Render(() => Form(model)[Input(() => model.Name)])</c>. Returning <c>null</c> renders
    ///     nothing — for a child built by its generated factory, that also drives it through its unmount path.
    /// </summary>
    /// <param name="factory">Builds the component under test; invoked once per render.</param>
    /// <param name="services">
    ///     Services available to the component (constructor-injected framework services, your own
    ///     registrations). Defaults to an empty provider.
    /// </param>
    public static RenderedComponent Render(Func<Component?> factory, IServiceProvider? services = null)
    {
        ArgumentNullException.ThrowIfNull(factory);
        return new RenderedComponent(new TestRoot(factory), services ?? EmptyServices);
    }

    /// <summary>
    ///     Renders <paramref name="app" /> the way a host does — as the application root, with the whole
    ///     document composed around it. Use this to assert on the page rather than on the component: the
    ///     doctype, <c>&lt;html lang&gt;</c>, the <c>&lt;head&gt;</c> every mounted component contributed
    ///     to, and the <c>&lt;body&gt;</c> the app rendered into.
    ///     <code>
    ///     var page = RaskTest.RenderDocument(new App(), services);
    ///     Assert.Contains("&gt;My app&lt;/title&gt;", page.Html);   // the head block keys its tags, so match the body
    ///     </code>
    ///     <see cref="Render{T}(T, IServiceProvider)" /> is the one to use for everything else — it adds no
    ///     markup of its own, so an assertion about a component is not an assertion about a page.
    /// </summary>
    /// <typeparam name="T">The app root's type, inferred from <paramref name="app" />.</typeparam>
    /// <param name="app">The root component the host would mount.</param>
    /// <param name="services">
    ///     Services available to the app. Defaults to an empty provider.
    /// </param>
    public static RenderedComponent<T> RenderDocument<T>(T app, IServiceProvider? services = null)
        where T : Component
    {
        ArgumentNullException.ThrowIfNull(app);

        // The same wrapper Rask.Server / Rask.Wasm / Rask.Native install: it composes the shell from the
        // app's Shell / HtmlLang / BodyClass and catches anything the subtree throws. Going through it
        // rather than reimplementing the composition is the point — a test asserts what a browser gets.
        return new RenderedComponent<T>(new RootErrorBoundary(app), app, services ?? EmptyServices);
    }

    /// <summary>
    ///     A zero-markup component that hands <paramref name="capture" /> the <see cref="EditContext" /> the
    ///     surrounding form is using, so a test can assert validation state (<c>GetValidationMessages</c>,
    ///     <c>IsValidating</c>, <c>IsModified</c>) that never reaches the markup. Place it <b>inside</b> the
    ///     form's children — the context is ambient only within that subtree:
    ///     <code>
    ///     EditContext? ctx = null;
    ///     var page = RaskTest.Render(() => Form(model)[
    ///         Input(() => model.Name),
    ///         RaskTest.EditContextProbe(c => ctx = c)
    ///     ]);
    ///     </code>
    ///     The callback runs on every render, so <paramref name="capture" /> sees the current context.
    /// </summary>
    /// <param name="capture">Receives the ambient context during each render.</param>
    public static Component EditContextProbe(Action<EditContext> capture)
    {
        ArgumentNullException.ThrowIfNull(capture);
        return new EditContextProbe(capture);
    }

    private static readonly IServiceProvider EmptyServices = new EmptyServiceProvider();

    // An empty provider (resolves nothing) — the default when a test's component injects no services.
    // A tiny local type instead of Microsoft.Extensions.DependencyInjection.BuildServiceProvider, so the
    // shipped package takes no DI package dependency (Core already provides IServiceProvider).
    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    // Forwarding live-render root. The component under test is rendered as this root's only child, so it
    // reconciles through the normal live-render path (the same shape Rask.TestSupport's StubComponent uses)
    // and adds no markup of its own. The factory re-runs per render, so a test's tree reflects its current
    // state; Render(Component) passes a factory that returns the one instance every time.
    private sealed class TestRoot(Func<Component?> factory) : Component
    {
        protected override Component? Render()
        {
            var child = factory();
            if (child is null || LiveRenderContext.Current is not { } ctx)
            {
                return child;
            }

            // Adopt and mount the child, exactly as the framework's own two wrapper roots do for theirs
            // (RootErrorBoundary for the App, RouteChainRenderer for a page). RenderAsLiveRootCore fires
            // the lifecycle on the ROOT only — which here is this forwarding wrapper, not the component
            // under test — so a component handed to Render() as an object rendered forever without
            // OnMount or OnMountAsync ever running, leaving anything that loads asynchronously stuck on
            // its placeholder. A child the factory built through its generated factory has already been
            // adopted and notified by GetOrCreate inside this render; both calls below are no-ops for it.
            AdoptChild(child, RenderHandle);
            ctx.NotifyParameters(child, propsChanged: false);
            return child;
        }
    }
}
