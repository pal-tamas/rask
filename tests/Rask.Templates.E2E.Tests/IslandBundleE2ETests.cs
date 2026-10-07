using System.Text.Json;

namespace Rask.Templates.E2E.Tests;

/// <summary>
///     Every npm island runtime <c>rask new --islands</c> offers is INSTALLED and BUNDLED, not just compiled.
/// </summary>
/// <remarks>
///     <para>
///         <c>An_islands_host_compiles</c> passes <c>RaskExternalBuild=false</c>, so it never runs npm or
///         Vite — which is how a Lit scaffold with no default export shipped with this gate green. Here the
///         island build is left on: the scaffold's own package.json is installed, Vite bundles it, and the
///         manifest the client runtime resolves names through has to list the scaffolded island and point
///         at a chunk that is on disk.
///     </para>
///     <para>
///         A class of its own so xUnit runs it beside <see cref="TemplateBuildE2ETests"/> rather than after
///         it: one class is one collection, which is one core.
///     </para>
/// </remarks>
public sealed class IslandBundleE2ETests
{
    [Theory]
    [InlineData("react", "ReactCounter")]
    [InlineData("preact", "PreactCounter")]
    [InlineData("solid", "SolidCounter")]
    [InlineData("vue", "VueCounter")]
    [InlineData("svelte", "SvelteCounter")]
    [InlineData("lit", "LitBadge")]
    [InlineData("angular", "AngularCounter")]
    public async Task A_scaffolded_island_bundles(string runtime, string island)
    {
        Assert.SkipUnless(TemplateBuildE2ETests.Enabled, TemplateBuildE2ETests.SkipReason);
        var (feed, version) = await CliBuildE2E.LocalFeed.Value;
        var name = $"Bundle{runtime}";
        var work = TemplateBuildE2ETests.NewWorkingDirectory();
        var projectDirectory = Path.Combine(work, name);
        var bundle = Path.Combine(projectDirectory, "wwwroot", "_rask", "external");
        var manifest = Path.Combine(bundle, "manifest.json");

        try
        {
            var result = TemplateBuildE2ETests.Scaffold("server", projectDirectory, name, version, [runtime]);
            TemplateBuildE2ETests.Write(result, projectDirectory, feed);

            var (exit, output) = await CliBuildE2E.RunDotnet(
                $"build \"{Path.Combine(projectDirectory, name + ".csproj")}\" -warnaserror -m:1 -p:RaskSpaBuild=false");

            Assert.True(
                exit == 0,
                $"--template server --islands {runtime} does not bundle:\n{CliBuildE2E.Diagnostics(output)}");
            Assert.True(File.Exists(manifest), $"the {runtime} island build wrote no manifest at '{manifest}'.");
            using var table = JsonDocument.Parse(await File.ReadAllTextAsync(manifest, TestContext.Current.CancellationToken));
            Assert.True(
                table.RootElement.TryGetProperty(island, out var chunk),
                $"the manifest does not list '{island}': {table.RootElement.GetRawText()}");

            // The manifest holds URLs under the public base, and Vite puts chunks in a folder below it.
            const string PublicBase = "/_rask/external/";
            var url = chunk.GetString()!;
            Assert.StartsWith(PublicBase, url, StringComparison.Ordinal);
            Assert.True(
                File.Exists(Path.Combine(bundle, url[PublicBase.Length..].Replace('/', Path.DirectorySeparatorChar))),
                $"the manifest sends '{island}' to '{url}', and no such chunk is in '{bundle}'.");
        }
        finally
        {
            CliBuildE2E.TryDeleteDirectory(work);
        }
    }
}
