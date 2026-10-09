using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Live;

namespace Rask.Core.Routing;

/// <summary>
///     The page instance at each place in the route chain, kept by the <see cref="Router" /> so that a page
///     outlives the <see cref="Outlet" /> that shows it.
/// </summary>
/// <remarks>
///     <para>
///         A page is still created where its layout places the outlet, and only there — a layout that
///         withholds its <see cref="Outlet" /> keeps the page from being constructed at all. What moved here
///         is who REMEMBERS it. The outlet used to, and an outlet is a positional child of its layout: a
///         layout that renders one sibling more ahead of it — the breadcrumb that appears once the page
///         has a title — gets a new outlet, and with it a new page, whose title is not loaded yet, so the
///         breadcrumb goes away again. The page is remembered by its place in the chain instead.
///     </para>
///     <para>
///         Each page is registered as a child of the page above it (the first one of the router), which is
///         what keeps it mounted and what unmounts it before its layout when both go.
///     </para>
/// </remarks>
internal sealed class RoutePageInstances(Router router)
{
    private Component?[] _pages = [];

    [UnconditionalSuppressMessage("Trimming", "IL2067",
        Justification = "Page types reach the route chain through Route.PageType / RouteRegistration.PageType, " +
                        "which are annotated with PublicConstructors | PublicProperties. The generated route " +
                        "registry initialiser also emits a [DynamicDependency(All, typeof(TPage))] per registered " +
                        "page, so ActivatorUtilities.CreateInstance is safe.")]
    public Component At(int index, Type type, int chainLength, LiveRenderContext ctx)
    {
        if (_pages.Length != chainLength)
        {
            Array.Resize(ref _pages, chainLength);
        }

        if (_pages[index] is not { IsTornDown: false } page || page.GetType() != type)
        {
            page = (Component)ActivatorUtilities.CreateInstance(ctx.Services!, type);
            _pages[index] = page;

            // The same type under a different layout is a different page: whatever sat below goes with it.
            Array.Clear(_pages, index + 1, _pages.Length - index - 1);
        }

        // Again on every walk: a parent rebuilds its child map each time it renders.
        var owner = index == 0 ? router : _pages[index - 1]!;
        owner.AdoptChild(page, ctx.Handle);
        return page;
    }
}
