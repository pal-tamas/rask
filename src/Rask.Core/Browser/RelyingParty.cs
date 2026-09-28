using System.Text.Json.Serialization;

namespace Rask.Core.Browser;

// All binary fields (challenge, ids, attestation/assertion buffers) cross the interop boundary as
// base64url strings — the framework's __raskWebAuthn helper encodes/decodes the ArrayBuffers at the seam.
// That matches how a relying-party backend exchanges and verifies these values, so the strings can be
// POSTed as-is.

/// <summary>The relying party (your site) for a WebAuthn ceremony.</summary>
/// <param name="Name">Human-readable site name shown in the platform UI.</param>
/// <param name="Id">
///     The RP id — a domain the current origin is a registrable suffix of (defaults to the origin's domain
///     when omitted).
/// </param>
public sealed record RelyingParty(
    string Name,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Id = null);
