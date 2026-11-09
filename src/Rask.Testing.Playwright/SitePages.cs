using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using Rask.Core.Routing;

namespace Rask.Testing;

/// <summary>
///     The paths of an app's pages, for a test that walks every one: <c>RaskSite.Pages&lt;Program&gt;()</c>, in path order.
/// </summary>
/// <remarks>
///     Every page the app's route table can open with nothing filled in — <c>/orders</c>, <c>/reports/{year?}</c> as
///     <c>/reports</c>. A page that needs a value (<c>/orders/{id}</c>) is left out, since only the test knows an id that
///     exists; it adds one with <see cref="And" />. A catch-all — the not-found page — is no page to walk.
/// </remarks>
public sealed class SitePages : IEnumerable<string>
{
    private readonly string[] _paths;

    internal SitePages(IEnumerable<string> paths) =>
        _paths = [.. paths.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];

    /// <summary>These pages and <paramref name="paths" />: the pages that need a value, with one filled in — <c>.And("/orders/1")</c>.</summary>
    /// <param name="paths">Paths to walk as well.</param>
    /// <returns>A new list; this one is unchanged.</returns>
    public SitePages And(params string[] paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return new SitePages(_paths.Concat(paths));
    }

    /// <summary>These pages but <paramref name="paths" />: a page a walk must not open — <c>.Without("/logout")</c>.</summary>
    /// <param name="paths">Paths to leave out.</param>
    /// <returns>A new list; this one is unchanged.</returns>
    public SitePages Without(params string[] paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return new SitePages(_paths.Except(paths, StringComparer.Ordinal));
    }

    /// <inheritdoc />
    public IEnumerator<string> GetEnumerator() => ((IEnumerable<string>)_paths).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>The pages <paramref name="app" /> declares, from the route table its generated registry fills.</summary>
    internal static SitePages Of(Assembly app)
    {
        // The registry registers from a module initializer, which runs on the first call into the assembly — and a test
        // that only names typeof(Program) has made none.
        RuntimeHelpers.RunModuleConstructor(app.ManifestModule.ModuleHandle);
        return From(RouteRegistry.BuildTree(app));
    }

    /// <summary>The pages of a route table, joined through their layouts the way the router joins them.</summary>
    internal static SitePages From(IReadOnlyList<Route> table) =>
        new(RouteFlattener.Flatten(table).Where(leaf => !leaf.HasCatchAll).Select(PathOf).OfType<string>());

    // Null for a route that needs a value; an optional one is left empty, as a link without it would be.
    private static string? PathOf(RouteLeaf leaf)
    {
        var segments = new List<string>(leaf.Pattern.Segments.Count);
        foreach (var segment in leaf.Pattern.Segments)
        {
            if (segment.Kind == SegmentKind.Literal)
            {
                segments.Add(segment.Literal);
            }
            else if (!segment.Optional)
            {
                return null;
            }
        }

        return "/" + string.Join('/', segments);
    }
}
