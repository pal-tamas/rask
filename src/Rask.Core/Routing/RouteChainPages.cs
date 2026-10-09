using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Live;

namespace Rask.Core.Routing;

/// <summary>
///     The page instances of the matched route chain, layout first and leaf last, kept by the
///     <see cref="Router" /> that owns them.
/// </summary>
/// <remarks>
///     The router mounts the WHOLE chain before the outermost layout renders, which is what lets that
///     layout read the leaf's <see cref="Component.PageTitle" /> in the render that first shows it. An
///     <see cref="Outlet" /> only places the page that is already here.
/// </remarks>
internal sealed class RouteChainPages
{
    private Component[] _pages = [];

    // A page reused across two URLs that bind the same values (`/todos` and `/todos/new`) still has to
    // hear that the URL moved, or its render cache serves the page it drew for the last one.
    private string? _boundPath;

    public Component[] Pages => _pages;

    [UnconditionalSuppressMessage("Trimming", "IL2072",
        Justification = "Page types reach the route chain through Route.PageType / RouteRegistration.PageType, " +
                        "which are annotated with PublicConstructors | PublicProperties. The generated route " +
                        "registry initialiser also emits a [DynamicDependency(All, typeof(TPage))] per registered " +
                        "page, so ActivatorUtilities.CreateInstance and PageBinder property reflection are safe.")]
    public void Mount(Component owner, LiveRenderContext ctx, RouteRenderState route)
    {
        var chain = route.Chain;
        if (_pages.Length != chain.Count)
        {
            Array.Resize(ref _pages, chain.Count);
        }

        // A page keeps its instance while every page above it kept its own: the same type under a
        // different layout is a different page.
        var kept = true;
        for (var i = 0; i < chain.Count; i++)
        {
            kept = kept && _pages[i] is { IsTornDown: false } page && page.GetType() == chain[i];
            if (!kept)
            {
                _pages[i] = (Component)ActivatorUtilities.CreateInstance(ctx.Services!, chain[i]);
            }
        }

        // Leaf first, so the pages leave in that order too: a page unmounts before the layout around it.
        for (var i = _pages.Length - 1; i >= 0; i--)
        {
            owner.AdoptChild(_pages[i], ctx.Handle);
        }

        var moved = !string.Equals(_boundPath, route.Path, StringComparison.Ordinal);
        foreach (var page in _pages)
        {
            var changed = PageBinder.Bind(page, route.Values, route.Query);
            LiveRenderContext.NotifyParameters(page, changed || moved);
        }

        _boundPath = route.Path;
    }

    /// <summary>The title of the deepest page that declares one.</summary>
    public string? Title()
    {
        for (var i = _pages.Length - 1; i >= 0; i--)
        {
            if (_pages[i].PageTitleInternal is { } title)
            {
                return title;
            }
        }

        return null;
    }

    public void Clear()
    {
        _pages = [];
        _boundPath = null;
    }
}
