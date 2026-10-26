using System.Text.Json;
using Rask.Cli.Scaffolding;
using Semver;

namespace Rask.Cli.Tests;

// The releases `rask new` falls back on when the registry does not answer (npm.snapshot.json): every template and
// island it can scaffold must come out of them pinned, or an offline scaffold writes a "latest" npm may refuse.
public sealed class NpmSnapshotTests
{
    private const string Root = "/tmp/app";

    private static readonly NpmRegistry Unreachable = new((_, _) => Task.FromResult<IReadOnlyList<NpmRelease>?>(null));

    public static TheoryData<string> Scaffolds() =>
    [
        .. SpaFramework.All.Select(framework => framework.Key),
        .. IslandRuntimes.All.Where(runtime => runtime != IslandRuntimes.Blazor).Select(runtime => "islands:" + runtime),
    ];

    [Theory]
    [MemberData(nameof(Scaffolds))]
    public async Task Every_scaffold_is_pinned_from_the_shipped_snapshot_with_no_registry(string scaffold)
    {
        var result = Generate(scaffold);

        var (pinned, warning) = await FrontEndVersions.PinAsync(result, Unreachable, CancellationToken.None);

        var manifests = pinned.Files.Where(f => Path.GetFileName(f.Path) == "package.json").Select(f => f.Content).ToList();
        Assert.NotEmpty(manifests);
        Assert.All(manifests, manifest => Assert.False(
            manifest.Contains("latest", StringComparison.Ordinal),
            $"{scaffold} floats a package the snapshot cannot pin; run scripts/upstream/front-ends.py ({warning}):\n{manifest}"));
        Assert.Contains(NpmSnapshot.Followed.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), warning, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_snapshot_answers_with_releases_newest_first_and_the_vite_each_toolchain_bundles()
    {
        using var document = JsonDocument.Parse("""
            {
              "followed": "2026-10-24",
              "packages": {
                "typescript": { "versions": { "6.0.3": {}, "7.0.2": { "engines": { "node": ">=20" } } } },
                "vite-plus": { "versions": { "1.4.0": { "bundledVersions": { "vite": "8.3.5" } } } }
              }
            }
            """);

        var (followed, registry) = NpmSnapshot.Read(document.RootElement);

        Assert.Equal(new DateOnly(2026, 10, 24), followed);
        var typescript = await registry.ReleasesAsync("typescript", CancellationToken.None) ?? [];
        Assert.Equal(["7.0.2", "6.0.3"], typescript.Select(r => r.Version.ToString()));
        Assert.Equal(">=20", typescript[0].Node);
        Assert.Null(await registry.ReleasesAsync("left-pad", CancellationToken.None));
        Assert.Equal(
            SemVersion.Parse("8.3.5", SemVersionStyles.Strict),
            await registry.BundledViteAsync(SemVersion.Parse("1.4.0", SemVersionStyles.Strict), CancellationToken.None));
    }

    private static ScaffoldResult Generate(string scaffold) =>
        scaffold.StartsWith("islands:", StringComparison.Ordinal)
            ? ProjectGenerator.GenerateServer(Root, "Shop", new ServerBatteries(), "1.2.3", [scaffold["islands:".Length..]])
            : ProjectGenerator.GenerateSpa(Root, "Shop", SpaFramework.All.Single(f => f.Key == scaffold), new ServerBatteries(), "1.2.3");
}
