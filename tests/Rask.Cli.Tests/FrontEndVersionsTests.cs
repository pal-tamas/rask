using Rask.Cli.Scaffolding;
using Semver;

namespace Rask.Cli.Tests;

// A template names no version for what floats: `rask new` writes the chosen one, and leaves a hold exactly as written.
public sealed class FrontEndVersionsTests
{
    private const string Manifest = """
        {
          "name": "shop-client",
          "devDependencies": {
            "@vitejs/plugin-react": "latest",
            "tailwindcss": "^4.3.0",
            "vite": "latest"
          }
        }
        """;

    [Fact]
    public async Task Every_latest_becomes_a_caret_on_the_version_chosen_and_a_hold_is_left_alone()
    {
        var registry = Registry(new()
        {
            ["vite"] = [NpmCompatibleVersionsTests.Release("9.0.0"), NpmCompatibleVersionsTests.Release("8.4.1")],
            ["@vitejs/plugin-react"] = [NpmCompatibleVersionsTests.Release("6.2.0", ("vite", "^8.0.0"))],
        });

        var (result, warning) = await FrontEndVersions.PinAsync(Scaffold(), registry, CancellationToken.None);

        Assert.Null(warning);
        var manifest = result.Files.Single(f => f.Path.EndsWith("package.json", StringComparison.Ordinal)).Content;
        Assert.Contains("\"vite\": \"^8.4.1\"", manifest, StringComparison.Ordinal);
        Assert.Contains("\"@vitejs/plugin-react\": \"^6.2.0\"", manifest, StringComparison.Ordinal);
        Assert.Contains("\"tailwindcss\": \"^4.3.0\"", manifest, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_registry_that_does_not_answer_leaves_latest_and_says_so()
    {
        var registry = new NpmRegistry((_, _) => Task.FromResult<IReadOnlyList<NpmRelease>?>(null));

        var (result, warning) = await FrontEndVersions.PinAsync(Scaffold(), registry, CancellationToken.None);

        Assert.NotNull(warning);
        Assert.Contains("\"vite\": \"latest\"", result.Files[0].Content, StringComparison.Ordinal);
        Assert.Equal(Scaffold().Files[1], result.Files[1]);
    }

    [Fact]
    public void Only_a_package_json_is_read_for_floating_packages()
    {
        var files = Scaffold().Files;

        var floating = files.Select(FrontEndVersions.Floating).ToList();

        Assert.Equal(["@vitejs/plugin-react", "vite"], floating[0].Order(StringComparer.Ordinal));
        Assert.Empty(floating[1]);
    }

    // The three spellings of the Vite+ toolchain move together: vite-plus exact, and the alias at the same version in
    // both devDependencies and overrides — a caret would let vite-plus move on from the core the override holds.
    [Fact]
    public async Task The_vite_plus_toolchain_is_pinned_exactly_with_its_alias_in_step()
    {
        var registry = Registry(
            new()
            {
                ["vite-plus"] = [NpmCompatibleVersionsTests.Release("1.3.0"), NpmCompatibleVersionsTests.Release("1.2.0")],
                ["@vitejs/plugin-react"] = [NpmCompatibleVersionsTests.Release("6.2.0", ("vite", "^8.0.0"))],
            },
            bundledVite: "8.3.4");

        var (result, warning) = await FrontEndVersions.PinAsync(VitePlusScaffold(), registry, CancellationToken.None);

        Assert.Null(warning);
        var manifest = result.Files[0].Content;
        Assert.Contains("\"vite-plus\": \"1.3.0\"", manifest, StringComparison.Ordinal);
        Assert.Equal(2, manifest.Split("\"npm:@voidzero-dev/vite-plus-core@1.3.0\"").Length - 1);
        Assert.Contains("\"@vitejs/plugin-react\": \"^6.2.0\"", manifest, StringComparison.Ordinal);
        Assert.DoesNotContain("latest", manifest, StringComparison.Ordinal);
    }

    // The plugins' `vite` peer is met by the Vite the toolchain bundles, so they are held to that, as they were to vite.
    [Fact]
    public async Task A_plugin_that_wants_a_newer_vite_than_the_toolchain_bundles_waits_for_it()
    {
        var registry = Registry(
            new()
            {
                ["vite-plus"] = [NpmCompatibleVersionsTests.Release("1.3.0")],
                ["@vitejs/plugin-react"] =
                [
                    NpmCompatibleVersionsTests.Release("7.0.0", ("vite", "^9.0.0")),
                    NpmCompatibleVersionsTests.Release("6.2.0", ("vite", "^8.0.0")),
                ],
            },
            bundledVite: "8.3.4");

        var (result, warning) = await FrontEndVersions.PinAsync(VitePlusScaffold(), registry, CancellationToken.None);

        Assert.Null(warning);
        Assert.Contains("\"@vitejs/plugin-react\": \"^6.2.0\"", result.Files[0].Content, StringComparison.Ordinal);
    }

    // A registry that cannot say what the core bundles holds no plugin back, and the toolchain still moves as one.
    [Fact]
    public async Task Without_the_bundled_vite_the_plugins_float_and_the_toolchain_stays_in_step()
    {
        var registry = Registry(new()
        {
            ["vite-plus"] = [NpmCompatibleVersionsTests.Release("1.3.0")],
            ["@vitejs/plugin-react"] = [NpmCompatibleVersionsTests.Release("7.0.0", ("vite", "^9.0.0"))],
        });

        var (result, warning) = await FrontEndVersions.PinAsync(VitePlusScaffold(), registry, CancellationToken.None);

        Assert.Null(warning);
        var manifest = result.Files[0].Content;
        Assert.Contains("\"@vitejs/plugin-react\": \"^7.0.0\"", manifest, StringComparison.Ordinal);
        Assert.Equal(2, manifest.Split("\"npm:@voidzero-dev/vite-plus-core@1.3.0\"").Length - 1);
    }

    private static ScaffoldResult VitePlusScaffold() => new(
    [
        new ScaffoldFile("/apps/Shop/client/package.json", """
            {
              "devDependencies": {
                "@vitejs/plugin-react": "latest",
                "vite": "npm:@voidzero-dev/vite-plus-core@latest",
                "vite-plus": "latest"
              },
              "overrides": {
                "vite": "npm:@voidzero-dev/vite-plus-core@latest"
              }
            }
            """),
    ]);

    private static ScaffoldResult Scaffold() => new(
    [
        new ScaffoldFile("/apps/Shop/client/package.json", Manifest),
        new ScaffoldFile("/apps/Shop/client/README.json", """{"vite": "latest"}"""),
    ]);

    private static NpmRegistry Registry(Dictionary<string, IReadOnlyList<NpmRelease>> releases, string? bundledVite = null) =>
        new(
            (package, _) => Task.FromResult(releases.TryGetValue(package, out var found) ? found : null),
            bundledVite is null ? null : (_, _) => Task.FromResult<SemVersion?>(SemVersion.Parse(bundledVite, SemVersionStyles.Strict)));
}
