namespace Rask.Site.E2E.Tests.Infrastructure;

/// <summary>
///     The published <c>src/Rask.Site</c> bundle, served from the shared static-file host — the site as
///     GitHub Pages serves it: the landing page at <c>/</c> and the showcase at <c>/docs</c>.
/// </summary>
/// <remarks>
///     A plain file server, not an ASP.NET host. This used to run <c>Rask.Example.Wasm.Host</c>, a
///     separate project whose whole job was to serve the showcase's bundle in development. There is one
///     app now and Pages serves it statically, so booting it behind a host would have tested a shape
///     nothing deploys.
/// </remarks>
public sealed class WasmExampleAppFixture : StaticWwwrootHostFixture
{
    protected override string ProjectRelativePath => "src/Rask.Site";

    protected override string MissingBundleMessage(string wwwroot) =>
        $"Published src/Rask.Site not found at '{wwwroot}'. Publish it first — e.g. "
        + $"`dotnet publish src/Rask.Site -c {Configuration} -p:WasmBuildNative=false`.";
}
