using Rask.Core;

namespace Rask.Testing;

/// <summary>
///     Entry point for unit-testing Rask components. <see cref="Render" /> renders a component to HTML
///     with its live event handlers wired, and returns a <see cref="RenderedComponent" /> you can query
///     and drive (invoke handlers, re-render) — no browser, server, or WebSocket involved.
/// </summary>
public static class RaskTest
{
    /// <summary>
    ///     Renders <paramref name="component" /> as a live root and returns a handle to the result. The
    ///     component is wrapped in a forwarding root (so any component — including one that can't be a
    ///     page root — works), its event handlers are registered, and <see cref="RenderedComponent.Html" />
    ///     holds the initial markup.
    /// </summary>
    /// <param name="component">The component under test.</param>
    /// <param name="services">
    ///     Services available to the component (constructor-injected framework services, your own
    ///     registrations). Defaults to an empty provider.
    /// </param>
    public static RenderedComponent Render(Component component, IServiceProvider? services = null)
    {
        ArgumentNullException.ThrowIfNull(component);
        return new RenderedComponent(new TestRoot(component), services ?? EmptyServices);
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
    // and adds no markup of its own.
    private sealed class TestRoot(Component child) : Component
    {
        protected override Component? Render() => child;
    }
}
