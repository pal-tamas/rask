using System.Collections.Concurrent;
using Semver;

namespace Rask.Cli.Scaffolding;

/// <summary>
///     The newest releases of a set of npm packages that agree with each other's peer ranges.
/// </summary>
/// <remarks>
///     <para>
///         The bare <c>latest</c> of every package is not an install: the day vite ships a major, its plugins still
///         ask for the last one, and <c>npm install</c> refuses the pair (ERESOLVE). So every package starts at its
///         newest release and steps down only as far as the others' peer ranges make it — vite stays on the newest 8.x
///         until the plugin that needs it accepts 9, and then both move on the next scaffold, with nobody bumping a pin.
///     </para>
///     <para>
///         Choices only ever step down, so it ends. When no release of a package satisfies everyone, the package that
///         asks for the newest of it steps down one release instead: a plugin that already wants vite 9 waits for the
///         one that does not yet.
///     </para>
/// </remarks>
internal static class NpmCompatibleVersions
{
    private const int MaxSteps = 5_000;

    private static readonly ConcurrentDictionary<string, SemVersionRange?> Ranges = new(StringComparer.Ordinal);

    /// <summary>The chosen version of every package, or the conflict that made a choice impossible.</summary>
    internal sealed record Resolution(IReadOnlyDictionary<string, SemVersion> Versions, string? Conflict);

    /// <summary>
    /// 
    /// </summary>
    /// <param name="releases">Each package's installable releases, newest first (<see cref="NpmRegistry" />).</param>
    /// <param name="node">The Node LTS the app runs on: a release whose <c>engines.node</c> refuses it is never chosen.</param>
    internal static Resolution Choose(IReadOnlyDictionary<string, IReadOnlyList<NpmRelease>> releases, SemVersion node)
    {
        ArgumentNullException.ThrowIfNull(releases);

        var candidates = releases.ToDictionary(
            p => p.Key,
            p => (IReadOnlyList<NpmRelease>)[.. p.Value.Where(r => Accepts(r.Node, node))],
            StringComparer.Ordinal);
        if (candidates.FirstOrDefault(p => p.Value.Count == 0) is { Key: { } empty })
        {
            return Fail($"{empty} has no stable release that runs on Node {node}");
        }

        var chosen = candidates.Keys.ToDictionary(p => p, _ => 0, StringComparer.Ordinal);
        for (var step = 0; step < MaxSteps; step++)
        {
            if (FirstUnsatisfied(candidates, chosen) is not { } package)
            {
                return new Resolution(
                    chosen.ToDictionary(p => p.Key, p => candidates[p.Key][p.Value].Version, StringComparer.Ordinal), null);
            }

            var demands = Demands(candidates, chosen, package);
            var settled = FirstAtOrBelow(candidates[package], chosen[package], demands.Select(d => d.Range));
            if (settled >= 0)
            {
                chosen[package] = settled;
                continue;
            }

            // No release satisfies them all: the one asking for the newest steps down instead.
            var (dependent, _) = demands.MaxBy(d => NewestDemanded(candidates[package], d.Range));
            if (chosen[dependent] + 1 >= candidates[dependent].Count)
            {
                return Fail(Describe(package, demands, candidates, chosen));
            }

            chosen[dependent]++;
        }

        return Fail($"no agreement within {MaxSteps} steps");
    }

    // A package whose current choice some other choice's peer range refuses.
    private static string? FirstUnsatisfied(
        Dictionary<string, IReadOnlyList<NpmRelease>> candidates, Dictionary<string, int> chosen) =>
        chosen.Keys
            .Order(StringComparer.Ordinal)
            .FirstOrDefault(package => Demands(candidates, chosen, package)
                .Any(d => !d.Range.Contains(candidates[package][chosen[package]].Version)));

    private static List<(string Dependent, SemVersionRange Range)> Demands(
        Dictionary<string, IReadOnlyList<NpmRelease>> candidates, Dictionary<string, int> chosen, string package) =>
    [
        .. chosen
            .Where(c => !string.Equals(c.Key, package, StringComparison.Ordinal))
            .Select(c => (Dependent: c.Key, Range: candidates[c.Key][c.Value].Peers.TryGetValue(package, out var range) ? Parse(range) : null))
            .Where(d => d.Range is not null)
            .Select(d => (d.Dependent, d.Range!))
            .OrderBy(d => d.Dependent, StringComparer.Ordinal),
    ];

    private static int FirstAtOrBelow(IReadOnlyList<NpmRelease> releases, int from, IEnumerable<SemVersionRange> ranges)
    {
        var all = ranges.ToList();
        for (var i = from; i < releases.Count; i++)
        {
            if (all.TrueForAll(r => r.Contains(releases[i].Version)))
            {
                return i;
            }
        }

        return -1;
    }

    // The higher, the newer the release this range insists on; a range nothing satisfies insists most.
    private static int NewestDemanded(IReadOnlyList<NpmRelease> releases, SemVersionRange range)
    {
        for (var i = releases.Count - 1; i >= 0; i--)
        {
            if (range.Contains(releases[i].Version))
            {
                return releases.Count - i;
            }
        }

        return int.MaxValue;
    }

    private static bool Accepts(string? engine, SemVersion node) =>
        engine is null || Parse(engine) is not { } range || range.Contains(node);

    // A range npm's grammar cannot read (a URL, a workspace: or npm: spec) constrains nothing here; npm judges it.
    private static SemVersionRange? Parse(string range) =>
        Ranges.GetOrAdd(range, r => SemVersionRange.TryParseNpm(r, out var parsed) ? parsed : null);

    private static string Describe(
        string package,
        List<(string Dependent, SemVersionRange Range)> demands,
        Dictionary<string, IReadOnlyList<NpmRelease>> candidates,
        Dictionary<string, int> chosen) =>
        $"no release of {package} satisfies "
        + string.Join(", ", demands.Select(d => $"{d.Dependent}@{candidates[d.Dependent][chosen[d.Dependent]].Version} ({d.Range})"));

    private static Resolution Fail(string conflict) =>
        new(new Dictionary<string, SemVersion>(StringComparer.Ordinal), conflict);
}
