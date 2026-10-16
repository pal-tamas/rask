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
///     not answer leaves <c>"latest"</c> for npm to resolve on the first build, and says so.
/// </remarks>
internal static partial class FrontEndVersions
{
    internal const string Latest = "latest";

    private static readonly TimeSpan LookupBudget = TimeSpan.FromSeconds(20);

    private static readonly string[] DependencySections = ["dependencies", "devDependencies"];

    /// <summary>The scaffold with its floating versions chosen, and the one line to show when they could not be.</summary>
    internal static async Task<(ScaffoldResult Result, string? Warning)> PinAsync(
        ScaffoldResult result, NpmRegistry registry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(registry);

        var files = result.Files.ToList();
        string? warning = null;
        for (var i = 0; i < files.Count; i++)
        {
            if (Floating(files[i]) is not { Count: > 0 } packages)
            {
                continue;
            }

            var (content, problem) = await PinFileAsync(files[i].Content, packages, registry, cancellationToken).ConfigureAwait(false);
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
        string content, IReadOnlyList<string> packages, NpmRegistry registry, CancellationToken cancellationToken)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(LookupBudget);

        var lookups = await Task.WhenAll(packages.Select(async package =>
            (package, releases: await registry.ReleasesAsync(package, budget.Token).ConfigureAwait(false)))).ConfigureAwait(false);
        if (lookups.FirstOrDefault(l => l.releases is null) is { package: { } unanswered })
        {
            return (content, $"The npm registry did not answer for {unanswered}: the client's package.json keeps \"latest\", "
                + "which npm resolves on the first build. Run rask new online to get versions chosen to install together.");
        }

        // Every choice must run on the Node LTS the installers put on a machine (the Active LTS, followed daily).
        var node = new SemVersion(NodeRequirement.ScaffoldLine.Major, NodeRequirement.ScaffoldLine.Minor, NodeRequirement.ScaffoldLine.Build);
        var releases = lookups.ToDictionary(l => l.package, l => l.releases!, StringComparer.Ordinal);
        var resolution = NpmCompatibleVersions.Choose(releases, node);
        var toolchain = VitePlusToolchain.Uses(content) ? resolution.Versions.GetValueOrDefault(VitePlusToolchain.Package) : null;
        if (toolchain is not null
            && await registry.BundledViteAsync(toolchain, budget.Token).ConfigureAwait(false) is { } bundled)
        {
            // What answers the plugins' `vite` peer is the Vite the toolchain bundles: they are held to it as
            // they were held to vite itself, so a plugin that already wants the next major waits for the toolchain.
            releases[VitePlusToolchain.Vite] = [new NpmRelease(bundled, new Dictionary<string, string>(StringComparer.Ordinal))];
            resolution = NpmCompatibleVersions.Choose(releases, node);
        }

        if (resolution.Conflict is { } conflict)
        {
            return (content, $"The newest front-end packages do not install together yet ({conflict}): the client's "
                + "package.json keeps \"latest\" for npm to resolve.");
        }

        var pinned = LatestValue().Replace(content, m =>
            resolution.Versions.TryGetValue(m.Groups["name"].Value, out var version)
                ? $"{m.Groups["key"].Value}\"{Range(m.Groups["name"].Value, version, toolchain)}\""
                : m.Value);
        return (toolchain is null ? pinned : VitePlusToolchain.Pin(pinned, toolchain), null);
    }

    // A caret, except for the toolchain whose core the alias pins exactly (VitePlusToolchain): the two must not part.
    private static string Range(string package, SemVersion version, SemVersion? toolchain) =>
        toolchain is not null && string.Equals(package, VitePlusToolchain.Package, StringComparison.Ordinal)
            ? version.ToString()
            : $"^{version}";

    [GeneratedRegex(@"(?<key>""(?<name>[^""]+)""\s*:\s*)""latest""", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex LatestValue();
}
