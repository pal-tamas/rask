using Rask.Core.Routing;

namespace Rask.Example.Shared.Features;

/// <summary>Browser APIs section — <see cref="WebAuthnDemo" /> (<c>IWebAuthn</c> / passkeys).</summary>
[Route("browser/webauthn")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class WebAuthnPage : Component
{
    protected override RenderResult Head => Title()["Passkeys (WebAuthn) — Browser APIs — Rask"];

    protected override RenderResult Render() =>
    [
        PageHeader.Render(
            "Passkeys (WebAuthn)",
            "Register and sign in with a passkey — a platform biometric or roaming security key — instead of "
            + "a password, via IWebAuthn (the Web Authentication API). WebAuthn is a two-party protocol: your "
            + "backend issues the challenge and verifies the attestation/assertion. This wrapper covers the "
            + "browser half and hands back base64url strings ready to POST. Call from a user gesture; a "
            + "cancellation returns null."),
        CodeSample(
            ["WebAuthnDemo.cs"],
            Notes: "CreateAsync(options) registers a passkey and returns the AttestationResult; GetAsync(options) "
                + "authenticates and returns the AssertionResult — both null if cancelled. The demo generates the "
                + "challenge client-side and skips server verification; a real app gets the challenge from, and "
                + "verifies the result on, its backend.",
            Result: WebAuthnDemo())
    ];
}
