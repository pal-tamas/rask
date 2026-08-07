namespace Rask.Examples.E2E.Tests.Infrastructure;

// Boots the Cookie + WASM host (serves the WASM bundle + the cookie auth API) for the login round-trip E2E.
public sealed class WasmCookieAuthAppFixture : ExampleAppFixture
{
    protected override string ProjectRelativePath => "samples/Rask.Example.Auth.WasmCookie.Host";
    protected override TimeSpan ReadyTimeout => TimeSpan.FromSeconds(180);
}
