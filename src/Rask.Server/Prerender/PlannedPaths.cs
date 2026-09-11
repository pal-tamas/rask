using System.Collections.Frozen;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Diagnostics;
using Rask.Core.Live;
using Rask.Core.Routing;

namespace Rask.Server.Prerender;

/// <summary>
///     The paths the page cache may keep copies of: the host's literal routes, and whatever the app's
///     <see cref="IPrerenderPaths" /> say its parameterised routes expand to.
/// </summary>
/// <remarks>
///     <para>
///         <b>This set is the bound on everything the cache does.</b> A request for any other path is served
///         live, exactly as it always was, so what the cache holds, how much it renders in the background
///         and how large it can grow are all functions of what the app declared — never of what someone
///         asks for. Without it, <c>/products/{random}</c> in a loop would be a way to fill memory with
///         copies of a not-found page.
///     </para>
///     <para>
///         Read concurrently by every request and replaced whole, so a request sees one complete plan or
///         the next one, never a plan half-built.
///     </para>
/// </remarks>
internal sealed class PlannedPaths
{
    /// <summary>The longest path an <see cref="IPrerenderPaths" /> may supply.</summary>
    internal const int MaxPathLength = 2048;

    private const string Category = "Rask.Prerender";

    private volatile FrozenSet<string> _paths = FrozenSet<string>.Empty;

    /// <summary>How many paths are planned.</summary>
    internal int Count => _paths.Count;

    /// <summary>Whether <paramref name="path" /> — a route path, <c>PathBase</c> removed — is planned.</summary>
    internal bool Contains(string path) => _paths.Contains(path);

    /// <summary>
    ///     Rebuilds the plan from <paramref name="hostRoutes" /> and the app's <see cref="IPrerenderPaths" />.
    /// </summary>
    /// <param name="hostRoutes">The host's own route table, mounted applications excluded.</param>
    /// <param name="scopes">Where to resolve the app's sources; each refresh gets a scope of its own.</param>
    /// <param name="maxPaths">The most paths the plan keeps.</param>
    /// <returns>
    ///     Whether the plan was replaced. A source that cannot be resolved, or that throws — cancellation
    ///     included — leaves the previous plan in place, and nothing it does escapes: this runs in a background
    ///     service, where an exception stops the whole host.
    /// </returns>
    internal bool Refresh(IReadOnlyList<Route> hostRoutes, IServiceScopeFactory scopes, int maxPaths)
    {
        ArgumentNullException.ThrowIfNull(hostRoutes);
        ArgumentNullException.ThrowIfNull(scopes);

        var planned = new HashSet<string>(StringComparer.Ordinal);
        var truncated = false;

        foreach (var path in LiteralPaths(hostRoutes))
        {
            truncated |= !TryAdd(planned, path, maxPaths);
        }

        // A scope of its own, because an implementation is free to depend on something scoped — a
        // DbContext listing the slugs is the ordinary case — and a refresh is not a request.
        using (var scope = scopes.CreateScope())
        {
            IReadOnlyList<IPrerenderPaths> sources;
            try
            {
                sources = scope.ServiceProvider.GetServices<IPrerenderPaths>().ToList();
            }
            catch (Exception error)
            {
                // Resolving is where a missing connection string or an unregistered dependency surfaces —
                // before any source has been asked anything.
                RaskDiagnostics.ReportOnce(
                    "prerender:paths-unresolvable:" + error.GetType().FullName,
                    RaskLogLevel.Warning,
                    Category,
                    () => "The app's IPrerenderPaths could not be resolved, so the page cache kept the paths it "
                          + "had planned before.",
                    error);
                return false;
            }

            foreach (var source in sources)
            {
                IReadOnlyList<string> paths;
                try
                {
                    // Materialised inside the try: a lazy sequence throws when it is walked, not when it is
                    // returned.
                    paths = source.Paths().ToList();
                }
                catch (Exception error)
                {
                    // Keeping the previous plan is the useful outcome. Swapping in a plan without this
                    // source's paths would evict every copy they had, on a failure that is probably a
                    // database blip. Cancellation is caught too: a call inside Paths() that timed out is a
                    // source that failed, not a host that is stopping.
                    RaskDiagnostics.ReportOnce(
                        "prerender:paths-threw:" + source.GetType().FullName,
                        RaskLogLevel.Warning,
                        Category,
                        () => $"{source.GetType().Name}.Paths() threw, so the page cache kept the paths it "
                              + "had planned before. Pages this source supplies stay cached until it answers.",
                        error);
                    return false;
                }

                foreach (var path in paths)
                {
                    if (!IsValid(path))
                    {
                        RaskDiagnostics.ReportOnce(
                            "prerender:invalid-path:" + path,
                            RaskLogLevel.Warning,
                            Category,
                            () => $"{source.GetType().Name} supplied \"{path}\", which is not a route path "
                                  + "(rooted, no query, no fragment, no '..', at most "
                                  + $"{MaxPathLength} characters). It is not cached.");
                        continue;
                    }

                    truncated |= !TryAdd(planned, path, maxPaths);
                }
            }
        }

        if (truncated)
        {
            RaskDiagnostics.ReportOnce(
                "prerender:truncated",
                RaskLogLevel.Warning,
                Category,
                () => $"The app plans more than {maxPaths} pages; only the first {maxPaths} are cached. The "
                      + "rest are rendered for each request, as every page was before the cache.");
        }

        _paths = planned.ToFrozenSet(StringComparer.Ordinal);
        return true;
    }

    /// <summary>
    ///     The paths of the routes whose every segment is a literal, the catch-all not-found route excluded.
    /// </summary>
    internal static IEnumerable<string> LiteralPaths(IReadOnlyList<Route> hostRoutes)
    {
        foreach (var leaf in RouteFlattener.Flatten(hostRoutes))
        {
            if (RouteRegistry.IsFallbackTemplate(leaf.FullTemplate))
            {
                continue;
            }

            if (leaf.Pattern.Segments.All(static segment => segment.Kind == SegmentKind.Literal))
            {
                yield return leaf.FullTemplate;
            }
        }
    }

    /// <summary>Whether <paramref name="path" /> is a route path the cache may key a copy on.</summary>
    internal static bool IsValid(string? path)
    {
        if (string.IsNullOrEmpty(path) || path.Length > MaxPathLength || path[0] != '/')
        {
            return false;
        }

        foreach (var c in path)
        {
            if (c is '?' or '#' or '\\' || char.IsControl(c))
            {
                return false;
            }
        }

        foreach (var segment in path.Split('/'))
        {
            if (segment is "." or "..")
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryAdd(HashSet<string> planned, string path, int maxPaths)
    {
        if (planned.Contains(path))
        {
            return true;
        }

        if (planned.Count >= maxPaths)
        {
            return false;
        }

        planned.Add(path);
        return true;
    }
}
