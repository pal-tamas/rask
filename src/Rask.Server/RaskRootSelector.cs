using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Core.Live;
using Rask.Core.Routing;

namespace Rask.Server;

/// <summary>
///     Decides which application a request belongs to: the host's, or one mounted under its own prefix.
/// </summary>
/// <remarks>
///     <para>
///         Answers both halves together on purpose. A root and a route table have to agree — rendering the
///         console's root against the host's routes, or the reverse, produces a page that resolves
///         perfectly and shows the wrong thing — so there is one place that decides, keyed on the path.
///     </para>
///     <para>
///         It also closes a gap the GET path alone would leave open. The WebSocket endpoint is mapped once
///         per host rather than once per root, so a session RESUMING after a restart was rebuilt with
///         whichever root the first <c>UseRask</c> captured. A console session would have come back as the
///         host application, under the console's URL, and only after a restart — so nothing would have
///         reported it. Resume now asks this the same question the GET did, using the path recorded in the
///         resume record.
///     </para>
/// </remarks>
internal sealed class RaskRootSelector
{
    private readonly Func<IServiceProvider, Component> _hostFactory;
    private readonly (RaskMountedApp Mount, Func<IServiceProvider, Component> Factory)[] _mounts;
    private readonly IReadOnlyList<System.Reflection.Assembly> _mountedAssemblies;

    public RaskRootSelector(
        Func<IServiceProvider, Component> hostFactory,
        IReadOnlyList<RaskMountedApp> mounts)
    {
        _hostFactory = hostFactory;
        _mounts = mounts
            .Select(m => (m, Factory(m)))
            .ToArray();
        _mountedAssemblies = mounts.Select(m => m.RoutesFrom).Distinct().ToArray();
    }

    /// <summary>The mounts this host serves, for mapping their patterns as endpoints of their own.</summary>
    public IReadOnlyList<RaskMountedApp> Mounts => Array.ConvertAll(_mounts, m => m.Mount);

    /// <summary>The root to build for <paramref name="path" />.</summary>
    public Func<IServiceProvider, Component> FactoryFor(string? path) =>
        Match(path)?.Factory ?? _hostFactory;

    /// <summary>The route table <paramref name="path" /> resolves against.</summary>
    /// <remarks>
    ///     Built per call, never captured: the table changes under hot reload, and a host holding one
    ///     across requests would keep serving routes that have since been edited away. <c>RouteRegistry</c>
    ///     caches each tree, so this is a dictionary hit in the steady state.
    /// </remarks>
    public IReadOnlyList<Route> RoutesFor(string? path) =>
        Match(path) is { } hit
            ? RouteRegistry.BuildTree(hit.Mount.RoutesFrom)
            : RouteRegistry.BuildTreeExcept(_mountedAssemblies);

    private (RaskMountedApp Mount, Func<IServiceProvider, Component> Factory)? Match(string? path)
    {
        foreach (var candidate in _mounts)
        {
            if (candidate.Mount.Covers(path))
            {
                return candidate;
            }
        }

        return null;
    }

    // The error boundary wraps a mounted root for the same reason it wraps the host's: an uncaught
    // exception anywhere in the tree should render a fallback page, not an HTTP 500.
#pragma warning disable RASK014 // The root has no parent render context to construct it through.
    private static Func<IServiceProvider, Component> Factory(RaskMountedApp mount) =>
        sp => new RootErrorBoundary((Component)ActivatorUtilities.CreateInstance(sp, mount.Root));
#pragma warning restore RASK014
}
