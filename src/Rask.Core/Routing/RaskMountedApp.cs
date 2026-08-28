using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace Rask.Core.Routing;

/// <summary>
///     A second Rask application served by the same host under its own URL prefix, with its own document
///     and its own route table.
/// </summary>
/// <remarks>
///     <para>
///         The operator console is the case this exists for. It ships as a referenced assembly, and
///         <see cref="RouteRegistry" /> is process-wide, so its pages joined the host application's route
///         table simply by being referenced — which meant the host's root rendered them, inside the host's
///         document. Sharing a document is not a detail: the console's stylesheet then applies to the
///         host's own pages, and the host's <c>[NotFound]</c> answers a mistyped console URL.
///     </para>
///     <para>
///         Declared here in <c>Rask.Core</c> rather than in either package that cares, because neither can
///         see the other: <c>Rask.Dashboard</c> does not reference <c>Rask.Server</c>, and
///         <c>Rask.Server</c> must not reference the batteries the console reads. A descriptor in the
///         container is what lets the console ask to be mounted and the host do the mounting, with no
///         dependency between them.
///     </para>
/// </remarks>
public sealed class RaskMountedApp
{
    /// <summary>Describes an application to mount.</summary>
    /// <param name="root">The root component rendered for every path the mount serves.</param>
    /// <param name="pattern">The route pattern to serve it on, e.g. <c>/_rask/{**path}</c>.</param>
    /// <param name="routesFrom">
    ///     The assembly whose <c>[Route]</c> pages belong to this application. Its routes are removed from
    ///     the host application's table and are the only ones this mount can reach.
    /// </param>
    public RaskMountedApp(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        Type root,
        string pattern,
        Assembly routesFrom)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);
        ArgumentNullException.ThrowIfNull(routesFrom);

        Root = root;
        Pattern = pattern;
        RoutesFrom = routesFrom;
        Prefix = PrefixOf(pattern);
    }

    /// <summary>The root component rendered for every path this mount serves.</summary>
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    public Type Root { get; }

    /// <summary>The route pattern the host maps.</summary>
    public string Pattern { get; }

    /// <summary>The assembly whose <c>[Route]</c> pages this application owns.</summary>
    public Assembly RoutesFrom { get; }

    /// <summary>
    ///     The literal prefix of <see cref="Pattern" /> — <c>/_rask</c> for <c>/_rask/{**path}</c>.
    /// </summary>
    /// <remarks>
    ///     Used to decide which application a path belongs to when there is no endpoint to ask: a
    ///     WebSocket resuming a session knows only the path the session was on, and rebuilding it under
    ///     the wrong root would silently render the host application inside the mount's URL.
    /// </remarks>
    public string Prefix { get; }

    /// <summary>Whether <paramref name="path" /> is served by this mount.</summary>
    public bool Covers(string? path) =>
        path is not null
        && path.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)
        && (path.Length == Prefix.Length || path[Prefix.Length] == '/');

    // Everything up to the first route parameter. "/_rask/{**path}" -> "/_rask"; a pattern that is all
    // parameter yields "/", which Covers() then treats as matching everything — correct for a mount
    // deliberately taking the whole origin.
    private static string PrefixOf(string pattern)
    {
        var normalized = pattern.StartsWith('/') ? pattern : "/" + pattern;
        var brace = normalized.IndexOf('{', StringComparison.Ordinal);
        var literal = brace < 0 ? normalized : normalized[..brace];
        literal = literal.TrimEnd('/');
        return literal.Length == 0 ? "/" : literal;
    }
}
