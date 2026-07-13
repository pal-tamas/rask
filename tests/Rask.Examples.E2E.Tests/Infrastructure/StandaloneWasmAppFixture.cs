namespace Rask.Examples.E2E.Tests.Infrastructure;

/// <summary>
///     Serves the <em>published</em> <c>Rask.Example.Wasm</c> AppBundle from the shared static-file host
///     (<see cref="StaticWwwrootHostFixture" />) — the "any static host" (GitHub Pages) scenario, with no
///     Rask runtime in front of it. Publishing emits a complete <c>index.html</c> (populated SDK import
///     map) plus the fingerprinted, prebuilt .NET-WASM runtime (<c>-p:WasmBuildNative=false</c> skips the
///     relink, which is flaky across build environments); a plain file server then serves those bytes
///     exactly as a CDN/Pages host would.
///     <para>
///         This replaces the earlier WasmAppHost dev launcher, whose request-time index.html resolution
///         from the static-web-assets manifest was unreliable across SDK/build environments (it could serve
///         a 0-byte body for the index route, so the runtime never booted). Serving the published output
///         removes that variable while still exercising the WASM app booting under a non-Rask static host.
///     </para>
/// </summary>
public sealed class StandaloneWasmAppFixture : StaticWwwrootHostFixture
{
    protected override int Port => 5096;

    protected override string ProjectRelativePath => "samples/Rask.Example.Wasm";

    protected override string MissingBundleMessage(string wwwroot) =>
        $"Published {ProjectRelativePath} not found at '{wwwroot}'. This fixture relies on the main " +
        "test-suite build having published it (Rask.Example.Wasm.Host's nested publish). Build the " +
        "solution first — e.g. `dotnet build Rask.slnx -p:WasmBuildNative=false`.";

    /// <summary>
    ///     Fail fast (and descriptively) if the published bundle is missing its baked scoped-JS bundle.
    ///     The single concatenated scoped-JS bundle is written by <c>BakeScopedAssetsTask</c> into the
    ///     published <c>wwwroot/_rask/a/{hash}.js</c>; without it every CodeSample page 404s on
    ///     <c>window.Rask.CodeSample</c> and highlighting never runs — which would otherwise surface as
    ///     several ~5s "locator never visible" timeouts with no hint at the cause.
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
                "BakeScopedAssetsTask did not emit /_rask/a/*.js — standalone WASM would 404 on the scoped " +
                "bundle and highlight/JS-interop would time out. See Rask.Wasm/build/Rask.Wasm.targets.");
        }
    }
}
