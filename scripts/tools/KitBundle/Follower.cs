namespace KitBundle;

/// <summary>
///     Follows the packages an engine bundles, and rebuilds the engine with them (scripts/upstream/followers.json,
///     daily).
/// </summary>
/// <remarks>
///     <para>
///         Every pin follows npm's latest, unless its engine says otherwise (intl-tel-input: the release fluxui.dev
///         serves). A family moves only as one: until every <c>@tiptap/</c> package is at one release, the pins stay.
///         bundle.json's overrides are holds a person made, and never move.
///     </para>
///     <para>
///         The tree is resolved afresh on every run, pins moved or not: a fix to a package only some dependency pulls
///         in moves the lock and nothing else, and a lock that resolves to the same tree builds the same bytes.
///     </para>
///     <para>
///         Every visitor of a page with an engine downloads it, so a bigger one is the owner's call: when a bundle
///         comes out bigger than the one committed — its budget — the run fails, naming both sizes, and the daily
///         job opens its issue. Nothing here re-records a budget.
///     </para>
/// </remarks>
internal sealed class Follower(Engine engine)
{
    private string Label => $"kit bundle {engine.Name}";

    /// <summary>Moves the pins, re-locks and rebuilds; with <paramref name="check" />, only fails when a pin is behind.</summary>
    internal async Task<int> Run(bool check)
    {
        var manifest = BundleManifest.Read(engine.ManifestPath);
        var followed = manifest.Followed.ToList();
        var wanted = (await Task.WhenAll(followed.Select(Wanted))).ToDictionary(r => r.Name, StringComparer.Ordinal);

        if (engine.Family is { } family)
        {
            var versions = wanted.Values.Where(r => r.Name.StartsWith(family, StringComparison.Ordinal)).Select(r => r.Version.ToString()).Distinct().ToList();
            if (versions.Count > 1)
            {
                Console.WriteLine($"{Label}: npm's {family}* are at {string.Join(", ", versions)}, not one version yet; the pins stay.");
                return 0;
            }
        }

        var moved = followed.Where(name => !string.Equals(manifest.Pin(name), wanted[name].Version.ToString(), StringComparison.Ordinal)).ToList();
        var named = string.Join(", ", moved.Select(name => $"{name} {manifest.Pin(name)} -> {wanted[name].Version}"));
        if (check)
        {
            if (moved.Count > 0)
            {
                throw new KitBundleException($"not what it follows: {named} — run: scripts/tools/KitBundle/follow.sh {engine.Name}");
            }

            Locked(BundleLock.Read(engine.LockPath), manifest, wanted);
            Console.WriteLine($"{Label}: every pin is what it follows.");
            return 0;
        }

        var budget = Sizes();
        foreach (var name in moved)
        {
            manifest.Move(name, wanted[name].Version.ToString());
        }

        // Everything the run needs is asked for before anything is written: a registry that stops answering
        // halfway leaves the committed files as they were.
        var tree = await TreeResolver.Resolve(manifest);
        Locked(tree, manifest, wanted);
        await Engine.Prefetch(tree);
        manifest.Write();
        BundleLock.Write(engine.LockPath, tree);
        await engine.Build();

        var built = Sizes();
        var summary = moved.Count > 0 ? named : "the tree re-resolved";
        var grown = engine.Bundles.Where(b => built[b] > budget[b]).ToList();
        if (grown.Count > 0)
        {
            throw new KitBundleException(
                $"{summary} grows {string.Join(", ", grown.Select(b => $"{b} from {budget[b]} to {built[b]} bytes"))}. "
                + "A bigger engine is the owner's call, so it does not land by itself: look at the regenerated tree and land it by hand.");
        }

        Console.WriteLine($"{Label}: {summary}; {string.Join(", ", engine.Bundles.Select(b => $"{b} {budget[b]} -> {built[b]} bytes"))}.");
        return 0;
    }

    private async Task<NpmRelease> Wanted(string name) =>
        await NpmRegistry.Release(name, await engine.Follows(name) ?? "latest");

    // The lock holds, for every pin, the release it follows and the registry's integrity of that tarball.
    private static void Locked(IEnumerable<LockedPackage> tree, BundleManifest manifest, Dictionary<string, NpmRelease> wanted)
    {
        var locked = tree.ToDictionary(p => p.Path, StringComparer.Ordinal);
        foreach (var (name, release) in wanted)
        {
            var path = (manifest.Data.ContainsKey(name) ? BundleLock.DataPrefix : "node_modules/") + name;
            var entry = locked.GetValueOrDefault(path);
            if (entry is null
                || !string.Equals(entry.Version, release.Version.ToString(), StringComparison.Ordinal)
                || !string.Equals(entry.Integrity, release.Integrity, StringComparison.Ordinal))
            {
                throw new KitBundleException(
                    $"bundle-lock.json locks {name} {entry?.Version} ({entry?.Integrity}), not {release.Version} ({release.Integrity})");
            }
        }
    }

    private Dictionary<string, long> Sizes() =>
        engine.Bundles.ToDictionary(b => b, b => new FileInfo(Path.Combine(Repository.Resources, b)).Length, StringComparer.Ordinal);
}
