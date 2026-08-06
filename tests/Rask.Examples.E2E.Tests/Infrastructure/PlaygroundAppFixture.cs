namespace Rask.Examples.E2E.Tests.Infrastructure;

/// <summary>
///     Serves the <em>published</em> <c>Rask.Example.Playground</c> bundle from the shared static-file host
///     (<see cref="StaticWwwrootHostFixture" />) — the live-playground sub-app as GitHub Pages serves it.
///     The playground compiles Rask C# in the browser with Roslyn, so it ships untrimmed and downloads its
///     own <c>_framework/*.dll</c> back as compiler references; a plain file server serves those bytes
///     exactly as a static host would. The CI e2e-build job publishes the bundle; a local run needs it
///     published first (in the same configuration this test is built as).
/// </summary>
public sealed class PlaygroundAppFixture : StaticWwwrootHostFixture
{
    protected override string ProjectRelativePath => "samples/Rask.Example.Playground";

    protected override string MissingBundleMessage(string wwwroot) =>
        $"Published {ProjectRelativePath} not found at '{wwwroot}'. Publish it first — e.g. " +
        $"`dotnet publish samples/Rask.Example.Playground -c {Configuration} -p:WasmBuildNative=false`.";
}
