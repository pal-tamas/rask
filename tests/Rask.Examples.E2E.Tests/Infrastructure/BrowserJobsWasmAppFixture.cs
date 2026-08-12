namespace Rask.Examples.E2E.Tests.Infrastructure;

/// <summary>
///     Serves the <em>published</em> <c>Rask.Example.Wasm.Jobs</c> bundle from the shared static-file host
///     — a WASM app running Rask.Jobs against a real SQLite database inside the browser, with no server
///     behind it at all.
/// </summary>
/// <remarks>
///     Unlike every other WASM fixture, this bundle <b>cannot</b> be published with
///     <c>-p:WasmBuildNative=false</c>: SQLite is a native library, and skipping the emscripten relink
///     produces a bundle with no <c>e_sqlite3</c> in it. <c>scripts/run-e2e-local.sh</c> therefore
///     publishes this one project with the relink, which is why it is the slowest line in that script.
/// </remarks>
public sealed class BrowserJobsWasmAppFixture : StaticWwwrootHostFixture
{
    protected override string ProjectRelativePath => "samples/Rask.Example.Wasm.Jobs";

    protected override string MissingBundleMessage(string wwwroot) =>
        $"Published {ProjectRelativePath} not found at '{wwwroot}'. Unlike the other WASM samples this one " +
        "needs the native SQLite relink, so it must be published WITHOUT -p:WasmBuildNative=false: " +
        "`dotnet publish samples/Rask.Example.Wasm.Jobs -c Release`. scripts/run-e2e-local.sh does this.";

    /// <summary>
    ///     Fail fast if the bundle was published without the native relink — otherwise the only symptom is
    ///     the app booting normally and then timing out on a job that can never run, because opening the
    ///     database throws deep inside SQLitePCLRaw.
    /// </summary>
    protected override void OnBundleLocated(string wwwroot)
    {
        var framework = Path.Combine(wwwroot, "_framework");
        var native = Directory.Exists(framework)
            ? Directory.EnumerateFiles(framework, "dotnet.native.*wasm").FirstOrDefault()
            : null;

        if (native is null)
        {
            throw new InvalidOperationException(
                $"Published {ProjectRelativePath} has no dotnet.native.*.wasm under '{framework}'. It was " +
                "published without the native build, so SQLite is not linked in and every database call " +
                "will fail. Re-publish without -p:WasmBuildNative=false.");
        }
    }
}
