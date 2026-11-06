namespace KitBundle;

/// <summary>
///     The node_modules tree npm would install for a <see cref="BundleManifest" />, resolved from the registry alone.
/// </summary>
/// <remarks>
///     <para>
///         npm's placement, as far as an engine needs it: a direct dependency at the root; every other package
///         hoisted to the root when nothing is there, and nested under the package that asked for it when the root
///         holds a release its range refuses. A dependency is found the way Node finds it — the nearest
///         <c>node_modules</c> walking up — so a release already in the tree is reused when the range accepts it.
///     </para>
///     <para>
///         Peers are installed as npm 7 and later installs them (unless marked optional), found from the
///         dependent's PARENT, since they must be the very copy the dependent's own host resolves. Optional
///         dependencies are locked with the systems they run on and installed only on those. An override holds a
///         package at its release wherever the tree asks for it.
///     </para>
///     <para>
///         Breadth-first in name order, so the same registry answers make the same tree.
///     </para>
/// </remarks>
internal static class TreeResolver
{
    private const string Modules = "node_modules/";

    internal static async Task<IReadOnlyList<LockedPackage>> Resolve(BundleManifest manifest)
    {
        var tree = new Dictionary<string, (NpmRelease Release, bool Optional)>(StringComparer.Ordinal);
        var queue = new Queue<(string Path, NpmRelease Release, bool Optional)>();

        // Every root package is asked for at once: the registry answers in parallel, the tree is placed in order.
        var roots = manifest.Dependencies.OrderBy(d => d.Key, StringComparer.Ordinal).ToList();
        foreach (var (name, _) in roots)
        {
            _ = NpmRegistry.Packument(name);
        }

        foreach (var (name, spec) in roots)
        {
            var release = NpmVersions.Pick(await NpmRegistry.Packument(name), spec);
            Place(tree, queue, Modules + name, release, optional: false);
        }

        while (queue.Count > 0)
        {
            var (path, release, optional) = queue.Dequeue();
            var edges = Edges(release, path, manifest.Overrides).ToList();
            foreach (var edge in edges)
            {
                _ = NpmRegistry.Packument(edge.Name);
            }

            foreach (var edge in edges)
            {
                await Visit(tree, queue, edge, optional);
            }
        }

        var packages = tree.Select(p => new LockedPackage(
            p.Key, p.Value.Release.Name, p.Value.Release.Version.ToString(), p.Value.Release.Tarball, p.Value.Release.Integrity,
            p.Value.Optional, p.Value.Release.Os, p.Value.Release.Cpu));

        var data = await Task.WhenAll(manifest.Data.Select(async d =>
        {
            var release = NpmVersions.Pick(await NpmRegistry.Packument(d.Key), d.Value);
            return new LockedPackage(
                BundleLock.DataPrefix + d.Key, d.Key, release.Version.ToString(), release.Tarball, release.Integrity, false, [], []);
        }));

        return [.. packages, .. data];
    }

    private sealed record Edge(string Name, string Spec, string From, bool Optional);

    // What one package asks for, and where its search for each starts: its own node_modules, or for a peer its parent's.
    private static IEnumerable<Edge> Edges(NpmRelease release, string path, IReadOnlyDictionary<string, string> overrides)
    {
        string Spec(string name, string spec) => overrides.TryGetValue(name, out var held) ? held : spec;

        foreach (var (name, spec) in release.Dependencies.OrderBy(d => d.Key, StringComparer.Ordinal))
        {
            if (!release.OptionalDependencies.ContainsKey(name))
            {
                yield return new Edge(name, Spec(name, spec), path, Optional: false);
            }
        }

        foreach (var (name, spec) in release.OptionalDependencies.OrderBy(d => d.Key, StringComparer.Ordinal))
        {
            yield return new Edge(name, Spec(name, spec), path, Optional: true);
        }

        foreach (var (name, spec) in release.Peers.OrderBy(d => d.Key, StringComparer.Ordinal))
        {
            yield return new Edge(name, Spec(name, spec), Parent(path), Optional: false);
        }
    }

    private static async Task Visit(
        Dictionary<string, (NpmRelease Release, bool Optional)> tree,
        Queue<(string Path, NpmRelease Release, bool Optional)> queue,
        Edge edge,
        bool optionalParent)
    {
        var optional = optionalParent || edge.Optional;
        if (Find(tree, edge.From, edge.Name) is { } found)
        {
            if (NpmVersions.Satisfies(tree[found].Release.Version, edge.Spec))
            {
                // A package some required path reaches is required, however an optional one reached it first.
                if (!optional && tree[found].Optional)
                {
                    tree[found] = (tree[found].Release, false);
                }

                return;
            }
        }

        var release = NpmVersions.Pick(await NpmRegistry.Packument(edge.Name), edge.Spec);
        var hoisted = Modules + edge.Name;
        if (!tree.ContainsKey(hoisted))
        {
            Place(tree, queue, hoisted, release, optional);
            return;
        }

        var nested = (edge.From.Length == 0 ? string.Empty : edge.From + "/") + Modules + edge.Name;
        if (tree.TryGetValue(nested, out var there))
        {
            throw new KitBundleException(
                $"{edge.Name}@{edge.Spec}, asked for from {Describe(edge.From)}, conflicts with the {there.Release.Version} "
                + "already there; npm would refuse this tree too (ERESOLVE)");
        }

        Place(tree, queue, nested, release, optional);
    }

    private static void Place(
        Dictionary<string, (NpmRelease Release, bool Optional)> tree,
        Queue<(string Path, NpmRelease Release, bool Optional)> queue,
        string path,
        NpmRelease release,
        bool optional)
    {
        tree[path] = (release, optional);
        queue.Enqueue((path, release, optional));
    }

    // Node's lookup: <from>/node_modules/<name>, then each enclosing package's, then the root's.
    private static string? Find(Dictionary<string, (NpmRelease Release, bool Optional)> tree, string from, string name)
    {
        for (var at = from; ; at = Parent(at))
        {
            var candidate = (at.Length == 0 ? string.Empty : at + "/") + Modules + name;
            if (tree.ContainsKey(candidate))
            {
                return candidate;
            }

            if (at.Length == 0)
            {
                return null;
            }
        }
    }

    // "node_modules/a/node_modules/b" -> "node_modules/a"; "node_modules/a" -> "" (the root).
    private static string Parent(string path)
    {
        var at = path.LastIndexOf("/" + Modules, StringComparison.Ordinal);
        return at < 0 ? string.Empty : path[..at];
    }

    private static string Describe(string from) => from.Length == 0 ? "the bundle" : from;
}
