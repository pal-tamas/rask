namespace Rask.Examples.E2E.Tests.Infrastructure;

public sealed class WasmExampleAppFixture : ExampleAppFixture
{
    protected override string ProjectRelativePath => "samples/Rask.Example.Wasm.Host";
    protected override int Port => 5098;

    // Serves the WASM showcase bundle; skip the flaky native relink (use the prebuilt runtime).
    protected override bool SkipWasmNativeRelink => true;
    protected override TimeSpan ReadyTimeout => TimeSpan.FromSeconds(180);
}
