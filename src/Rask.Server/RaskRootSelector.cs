using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Core.Live;
using Rask.Core.Routing;
using Rask.Server.DevTools;

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
    private readonly string _mountedKey;
    private readonly IRaskServerDevTools? _devTools;

    public RaskRootSelector(
        Func<IServiceProvider, Component> hostFactory,
        IReadOnlyList<RaskMountedApp> mounts,
        IRaskServerDevTools? devTools = null)
    {
        _hostFactory = hostFactory;
        _mounts = mounts
            .Select(m => (m, Factory(m)))
            .ToArray();
        _mountedAssemblies = mounts.Select(m => m.RoutesFrom).Distinct().ToArray();
        // Once, here: the host's table is asked for on every render of every host session's Router.
        _mountedKey = RouteRegistry.ExceptKey(_mountedAssemblies);
        _devTools = devTools;
    }

    /// <summary>Whether a session last seen at <paramref name="path" /> may be rebuilt from a resume record.</summary>
    /// <remarks>
    ///     Asked here because resume already comes here to learn which root to build, and a resume record is a way in
    ///     that skips the GET: a devtools panel decides per request who may open it, and a record carries none of what
    ///     that decision reads. Refusing makes the client reload, and the reload is a GET that is asked.
    /// </remarks>
    public bool CanResume(string path) => _devTools?.CanResume(path) ?? true;

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
    public IReadOnlyList<Route> RoutesFor(string? path) => TableFor(path)();

    /// <summary>
    ///     A provider of the route table for the application <paramref name="path" /> belongs to, for a session to keep.
    /// </summary>
    /// <remarks>
    ///     The application is decided once, from the path the session was opened at; the table itself is still built
    ///     on every call, for the hot-reload reason <see cref="RoutesFor" /> gives.
    /// </remarks>
    public Func<IReadOnlyList<Route>> TableFor(string? path)
    {
        if (Match(path) is { } hit)
        {
            var assembly = hit.Mount.RoutesFrom;
            return () => RouteRegistry.BuildTree(assembly);
        }

        var (mounted, key) = (_mountedAssemblies, _mountedKey);
        return () => RouteRegistry.BuildTreeExcept(mounted, key);
    }

    /// <summary>Whether two paths belong to the same application on this host: the host's, or the same mount.</summary>
    public bool SameApplication(string? path, string? other) =>
        ReferenceEquals(Match(path)?.Mount, Match(other)?.Mount);

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
