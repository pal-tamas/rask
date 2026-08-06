namespace Rask.Examples.E2E.Tests.Infrastructure;

/// <summary>
///     Serves the <em>published</em> <c>Rask.Example.Site</c> marketing landing app from the shared
///     static-file host (<see cref="StaticWwwrootHostFixture" />) — the GitHub Pages front-door scenario.
///     The landing page is itself a Rask WASM app, so this exercises the framework rendering a whole
///     document shell (RASK021) plus its live counter/tabs and the scoped hero-canvas module.
/// </summary>
public sealed class SiteWasmAppFixture : StaticWwwrootHostFixture
{
    protected override string ProjectRelativePath => "samples/Rask.Example.Site";

    protected override string MissingBundleMessage(string wwwroot) =>
        $"Published {ProjectRelativePath} not found at '{wwwroot}'. The E2E build publishes it explicitly " +
        "(see the 'Publish landing site sample' step in ci.yml's e2e-build job). Locally, run " +
        "`dotnet publish samples/Rask.Example.Site -c Release -p:WasmBuildNative=false` first.";

    /// <summary>
    ///     Fail fast if the baked scoped-JS bundle is missing — the hero canvas animation lives in the
    ///     sibling <c>App.js</c>, baked by <c>BakeScopedAssetsTask</c> into <c>wwwroot/_rask/a/{hash}.js</c>.
    /// </summary>
    protected override void OnBundleLocated(string wwwroot)
    {
        var scopedDir = Path.Combine(wwwroot, "_rask", "a");
        var jsFile = Directory.Exists(scopedDir)
            ? Directory.EnumerateFiles(scopedDir, "*.js").FirstOrDefault()
            : null;
        if (jsFile is null)
        {
            throw new InvalidOperationException(
                $"Published {ProjectRelativePath} is missing its baked scoped-JS bundle under '{scopedDir}'. " +
                "BakeScopedAssetsTask did not emit /_rask/a/*.js — the hero canvas would 404 on window.Rask.App.");
        }
    }
}
