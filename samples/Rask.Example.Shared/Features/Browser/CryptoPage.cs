using Rask.Core.Routing;

namespace Rask.Example.Shared.Features;

/// <summary>Browser APIs section — <see cref="CryptoDemo" /> (<c>ICrypto</c>).</summary>
[Route("browser/crypto")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class CryptoPage : Component
{
    protected override RenderResult Head => Title()["Web Crypto — Browser APIs — Rask"];

    protected override RenderResult Render() =>
    [
        PageHeader.Render(
            "Web Crypto",
            "Cryptographically strong randomness (UUIDs, nonces) and SHA hashing from C# via ICrypto (the "
            + "Web Crypto API). Works on both transports; needs a secure context (HTTPS or localhost)."),
        CodeSample(
            ["CryptoDemo.cs"],
            Notes: "RandomUuidAsync / RandomBytesAsync use crypto.randomUUID / getRandomValues; DigestHexAsync "
                + "hashes UTF-8 text with crypto.subtle.digest and returns lowercase hex.",
            Result: CryptoDemo())
    ];
}
