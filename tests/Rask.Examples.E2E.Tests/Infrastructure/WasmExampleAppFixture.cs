namespace Rask.Examples.E2E.Tests.Infrastructure;

public sealed class WasmExampleAppFixture : ExampleAppFixture
{
    protected override string ProjectRelativePath => "samples/Rask.Example.Wasm.Host";
    protected override TimeSpan ReadyTimeout => TimeSpan.FromSeconds(180);
}
