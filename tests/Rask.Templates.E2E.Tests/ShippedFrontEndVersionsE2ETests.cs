using Rask.Cli.Scaffolding;

namespace Rask.Templates.E2E.Tests;

/// <summary>
///     The versions <c>rask new</c> writes when the registry does not answer (<see cref="NpmSnapshot" />) resolve
///     with npm, for every front-end template and island runtime.
/// </summary>
/// <remarks>
///     The other gates install what the live registry chooses; this is the one that proves the shipped snapshot, which
///     scripts/upstream/front-ends.py refreshes daily and lands through these gates. npm resolves the tree
///     (<c>--package-lock-only</c>) and fetches nothing, which is where a set that does not install together fails:
///     ERESOLVE, as a bare <c>"latest"</c> did for Solid and Lit once @volar/typescript peered on <c>typescript &lt;7</c>.
/// </remarks>
public sealed class ShippedFrontEndVersionsE2ETests
{
    private static readonly NpmRegistry Unreachable = new((_, _) => Task.FromResult<IReadOnlyList<NpmRelease>?>(null));

    public static TheoryData<string> Scaffolds() =>
    [
        .. SpaFramework.All.Select(framework => framework.Key),
        .. IslandRuntimes.All.Where(runtime => runtime != IslandRuntimes.Blazor).Select(runtime => "islands:" + runtime),
    ];

    [Theory]
    [MemberData(nameof(Scaffolds))]
    public async Task The_shipped_versions_resolve_with_npm(string scaffold)
    {
        Assert.SkipUnless(TemplateBuildE2ETests.Enabled, TemplateBuildE2ETests.SkipReason);
        var work = TemplateBuildE2ETests.NewWorkingDirectory();
        var project = Path.Combine(work, "Shipped");
        var islands = scaffold.StartsWith("islands:", StringComparison.Ordinal) ? [scaffold["islands:".Length..]] : Array.Empty<string>();
        var key = islands.Length > 0 ? "server" : scaffold;

        try
        {
            var (pinned, warning) = await FrontEndVersions.PinAsync(
                TemplateBuildE2ETests.Scaffold(key, project, "Shipped", "1.0.0", islands), Unreachable, CancellationToken.None);
            var manifests = pinned.Files.Where(f => Path.GetFileName(f.Path) == "package.json").ToList();
            foreach (var manifest in manifests)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(manifest.Path)!);
                await File.WriteAllTextAsync(manifest.Path, manifest.Content, TestContext.Current.CancellationToken);
            }

            var resolved = new List<(string Directory, int Exit, string Output)>();
            foreach (var directory in manifests.Select(m => Path.GetDirectoryName(m.Path)!))
            {
                var (exit, output) = await CliBuildE2E.RunProcess(
                    "npm", ["install", "--package-lock-only", "--ignore-scripts", "--no-audit", "--no-fund"], directory);
                resolved.Add((directory, exit, output));
            }

            Assert.NotNull(warning);
            Assert.NotEmpty(manifests);
            Assert.All(manifests, m => Assert.DoesNotContain("latest", m.Content, StringComparison.Ordinal));
            Assert.All(resolved, r => Assert.True(r.Exit == 0, $"{scaffold}: the shipped versions do not resolve in {r.Directory}:\n{r.Output}"));
        }
        finally
        {
            CliBuildE2E.TryDeleteDirectory(work);
        }
    }
}
