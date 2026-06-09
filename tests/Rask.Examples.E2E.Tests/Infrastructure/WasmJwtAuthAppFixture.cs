namespace Rask.Examples.E2E.Tests.Infrastructure;

// Boots the JWT + WASM host (serves the WASM bundle + the JWT auth API) for the login round-trip E2E.
public sealed class WasmJwtAuthAppFixture : ExampleAppFixture
{
    protected override string ProjectRelativePath => "samples/Rask.Example.Auth.WasmJwt.Host";
    protected override int Port => 5105;
    protected override TimeSpan ReadyTimeout => TimeSpan.FromSeconds(180);
}
