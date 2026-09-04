namespace Rask.Examples.E2E.Tests.Infrastructure;

/// <summary>
///     Serves the <em>published</em> <c>Rask.Example.Site</c> marketing landing app from the shared
///     static-file host (<see cref="StaticWwwrootHostFixture" />) — the GitHub Pages front-door scenario.
///     The landing page is itself a Rask WASM app, so this exercises the framework composing a whole
///     document around a body-only root, plus its live counter/tabs and the scoped hero-canvas module.
/// </summary>
public sealed class SiteWasmAppFixture : StaticWwwrootHostFixture
{
    protected override string ProjectRelativePath => "samples/Rask.Example.Site";

    protected override string MissingBundleMessage(string wwwroot) =>
        $"Published {ProjectRelativePath} not found at '{wwwroot}'. scripts/run-e2e-local.sh publishes it " +
        "before running the journeys, so this usually means the suite was started directly. Run " +
        "`dotnet publish samples/Rask.Example.Site -c Release -p:WasmBuildNative=false` first, or use the script.";

    /// <summary>
    ///     Fail fast if the published page was not prerendered, or was prerendered over its boot shell.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         This used to require a baked scoped-JS bundle, because the hero was a canvas animation
    ///         driven by a sibling <c>App.ts</c>. The page ships no JavaScript of its own now, so there is
    ///         no bundle to look for — and the property worth guarding in its place is the one the whole
    ///         publish now turns on.
    ///     </para>
    ///     <para>
    ///         Both halves matter and they fail in opposite directions. Markup with no boot script is a
    ///         page that can never become interactive; a boot script with no markup is a spinner, which is
    ///         what prerendering exists to stop serving. Either one still renders something in a browser,
    ///         so the journey below would go green on the first and merely look slow on the second.
    ///     </para>
    /// </remarks>
    protected override void OnBundleLocated(string wwwroot)
    {
        var index = Path.Combine(wwwroot, "index.html");
        if (!File.Exists(index))
        {
            throw new InvalidOperationException($"Published {ProjectRelativePath} has no index.html at '{wwwroot}'.");
        }

        var html = File.ReadAllText(index);

        if (!html.Contains("Ship a whole product.", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Published {ProjectRelativePath} was not prerendered: index.html carries the boot shell "
                + "rather than the page. <RaskPrerender> is set, so the pass either wrote nothing (no "
                + "literal route in the table) or skipped this route — check the [Rask.Prerender] lines in "
                + "the publish log, which report the count and every skip.");
        }

        if (!html.Contains("main.js", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Published {ProjectRelativePath} lost its boot script: index.html has the prerendered "
                + "markup but no <script src=\"main.js\">, so the bundle can never take the page over and "
                + "nothing on it is interactive. The prerender pass is meant to splice into the shell, not "
                + "replace it.");
        }
    }
}
