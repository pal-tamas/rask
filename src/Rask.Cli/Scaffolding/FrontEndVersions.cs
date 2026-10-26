using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Semver;

namespace Rask.Cli.Scaffolding;

/// <summary>
///     Turns every <c>"latest"</c> in a scaffold's <c>package.json</c> files into the newest versions that install
///     together (<see cref="NpmCompatibleVersions" />), written as <c>^x.y.z</c>.
/// </summary>
/// <remarks>
///     The templates name no version for what floats, so nothing in the repo goes stale between releases and no
///     pull request moves it; a range in a template is a hold, and is left exactly as written. A registry that does
///     not answer — or whose newest releases do not install together — gives way to the releases this CLI shipped
///     with (<see cref="NpmSnapshot" />), chosen from the same way, and the scaffold says so: a bare <c>"latest"</c>
///     is not an install (npm can refuse it, ERESOLVE) and is left only when even the snapshot has no answer.
/// </remarks>
internal static partial class FrontEndVersions
{
    internal const string Latest = "latest";

    private static readonly string[] DependencySections = ["dependencies", "devDependencies"];

    /// <summary>The scaffold with its floating versions chosen, and the one line to show when they could not be.</summary>
    internal static Task<(ScaffoldResult Result, string? Warning)> PinAsync(
        ScaffoldResult result, NpmRegistry registry, CancellationToken cancellationToken) =>
        PinAsync(result, registry, NpmSnapshot.Registry, NpmSnapshot.Followed, cancellationToken);

    /// <summary>
    ///     <see cref="PinAsync(ScaffoldResult, NpmRegistry, CancellationToken)" />, choosing from
    ///     <paramref name="snapshot" /> (refreshed on <paramref name="followed" />) where <paramref name="registry" />
    ///     cannot be chosen from.
    /// </summary>
    internal static async Task<(ScaffoldResult Result, string? Warning)> PinAsync(
        ScaffoldResult result, NpmRegistry registry, NpmRegistry snapshot, DateOnly followed, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(snapshot);

        var files = result.Files.ToList();
        string? warning = null;
        for (var i = 0; i < files.Count; i++)
        {
            if (Floating(files[i]) is not { Count: > 0 } packages)
            {
                continue;
            }

            var (content, problem) = await PinFileAsync(files[i].Content, packages, (registry, snapshot, followed), cancellationToken)
                .ConfigureAwait(false);
            files[i] = files[i] with { Content = content };
            warning ??= problem;
        }

        return (result with { Files = files }, warning);
    }

    /// <summary>The packages a <c>package.json</c> leaves at <c>"latest"</c>; empty for any other file.</summary>
    internal static IReadOnlyList<string> Floating(ScaffoldFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (!string.Equals(Path.GetFileName(file.Path), "package.json", StringComparison.Ordinal))
        {
            return [];
        }

        using var document = JsonDocument.Parse(file.Content);
        return
        [
            .. DependencySections
                .Where(section => document.RootElement.TryGetProperty(section, out _))
                .SelectMany(section => document.RootElement.GetProperty(section).EnumerateObject())
                .Where(p => p.Value.ValueKind == JsonValueKind.String && string.Equals(p.Value.GetString(), Latest, StringComparison.Ordinal))
                .Select(p => p.Name)
                .Distinct(StringComparer.Ordinal),
        ];
    }

    private static async Task<(string Content, string? Warning)> PinFileAsync(
        string content,
        IReadOnlyList<string> packages,
        (NpmRegistry Live, NpmRegistry Snapshot, DateOnly Followed) registries,
        CancellationToken cancellationToken)
    {
        var (live, problem) = await ChooseAsync(content, packages, registries.Live, cancellationToken).ConfigureAwait(false);
        if (live is not null)
        {
            return (Write(content, live), null);
        }

        var (shipped, _) = await ChooseAsync(content, packages, registries.Snapshot, cancellationToken).ConfigureAwait(false);
        return shipped is null
            ? (content, $"{problem}: the client's package.json keeps \"latest\" for npm to resolve on the first build.")
            : (Write(content, shipped),
                $"{problem}: the client's package.json takes the newest versions known on {registries.Followed.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} "
                + "to install together. Run rask new online, or `npm update` in the client, for anything newer.");
    }

    // The newest versions of `packages` that install together, or why there are none.
    private static async Task<(Choice? Chosen, string? Problem)> ChooseAsync(
        string content, IReadOnlyList<string> packages, NpmRegistry registry, CancellationToken cancellationToken)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(registry.Budget);
        try
        {
            return await ChooseWithinAsync(content, packages, registry, budget.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return (null, "The npm registry did not answer in time");
        }
    }

    private static async Task<(Choice? Chosen, string? Problem)> ChooseWithinAsync(
        string content, IReadOnlyList<string> packages, NpmRegistry registry, CancellationToken budget)
    {
        var lookups = await Task.WhenAll(packages.Select(async package =>
            (package, releases: await registry.ReleasesAsync(package, budget).ConfigureAwait(false)))).ConfigureAwait(false);
        if (lookups.FirstOrDefault(l => l.releases is null) is { package: { } unanswered })
        {
            return (null, $"The npm registry did not answer for {unanswered}");
        }

        // Every choice must run on the Node LTS the installers put on a machine (the Active LTS, followed daily).
        var node = new SemVersion(NodeRequirement.ScaffoldLine.Major, NodeRequirement.ScaffoldLine.Minor, NodeRequirement.ScaffoldLine.Build);
        var releases = lookups.ToDictionary(l => l.package, l => l.releases!, StringComparer.Ordinal);
        var resolution = NpmCompatibleVersions.Choose(releases, node);
        var toolchain = VitePlusToolchain.Uses(content) ? resolution.Versions.GetValueOrDefault(VitePlusToolchain.Package) : null;
        if (toolchain is not null
            && await registry.BundledViteAsync(toolchain, budget).ConfigureAwait(false) is { } bundled)
        {
            // What answers the plugins' `vite` peer is the Vite the toolchain bundles: they are held to it as
            // they were held to vite itself, so a plugin that already wants the next major waits for the toolchain.
            releases[VitePlusToolchain.Vite] = [new NpmRelease(bundled, new Dictionary<string, string>(StringComparer.Ordinal))];
            resolution = NpmCompatibleVersions.Choose(releases, node);
        }

        return resolution.Conflict is { } conflict
            ? (null, $"The newest front-end packages do not install together yet ({conflict})")
            : (new Choice(resolution.Versions, toolchain), null);
    }

    private static string Write(string content, Choice choice)
    {
        var pinned = LatestValue().Replace(content, m =>
            choice.Versions.TryGetValue(m.Groups["name"].Value, out var version)
                ? $"{m.Groups["key"].Value}\"{Range(m.Groups["name"].Value, version, choice.Toolchain)}\""
                : m.Value);
        return choice.Toolchain is null ? pinned : VitePlusToolchain.Pin(pinned, choice.Toolchain);
    }

    // A caret, except for the toolchain whose core the alias pins exactly (VitePlusToolchain): the two must not part.
    private static string Range(string package, SemVersion version, SemVersion? toolchain) =>
        toolchain is not null && string.Equals(package, VitePlusToolchain.Package, StringComparison.Ordinal)
            ? version.ToString()
            : $"^{version}";

    [GeneratedRegex(@"(?<key>""(?<name>[^""]+)""\s*:\s*)""latest""", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex LatestValue();

    private sealed record Choice(IReadOnlyDictionary<string, SemVersion> Versions, SemVersion? Toolchain);
}
