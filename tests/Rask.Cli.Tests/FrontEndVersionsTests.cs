using Rask.Cli.Scaffolding;

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

    private static ScaffoldResult Scaffold() => new(
    [
        new ScaffoldFile("/apps/Shop/client/package.json", Manifest),
        new ScaffoldFile("/apps/Shop/client/README.json", """{"vite": "latest"}"""),
    ]);

    private static NpmRegistry Registry(Dictionary<string, IReadOnlyList<NpmRelease>> releases) =>
        new((package, _) => Task.FromResult(releases.TryGetValue(package, out var found) ? found : null));
}
