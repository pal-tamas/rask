namespace Rask.Examples.E2E.Tests.Infrastructure;

// Boots the JWT + WASM host (serves the WASM bundle + the JWT auth API) for the login round-trip E2E.
public sealed class WasmJwtAuthAppFixture : ExampleAppFixture
{
    protected override string ProjectRelativePath => "samples/Rask.Example.Auth.WasmJwt.Host";
    protected override int Port => 5105;

    protected override bool SkipWasmNativeRelink => true;
    protected override TimeSpan ReadyTimeout => TimeSpan.FromSeconds(180);

    // The host fails fast without Jwt:Key outside Development (the public DevKey would let anyone
    // forge tokens). Supply a test-only signing key so the production-mode E2E host can boot.
    protected override IReadOnlyDictionary<string, string> ExtraEnvironment { get; } =
        new Dictionary<string, string>
        {
            ["Jwt__Key"] = "rask-e2e-test-signing-key-not-for-production-0123456789abcdef"
        };
}
