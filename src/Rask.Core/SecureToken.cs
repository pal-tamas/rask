using System.Security.Cryptography;

namespace Rask.Core;

internal static class SecureToken
{
    // A 128-bit cryptographically-random token as 32 lowercase hex chars. Used for values whose
    // secrecy IS the security boundary — redeem tickets (the authority to set the auth cookie), live
    // session ids (the bearer for the WS / upload / download endpoints), and the first-run admin
    // token (the authority to claim an unclaimed instance). Replaces Guid.NewGuid().ToString("N"): a
    // v4 GUID exposes only ~122 random bits and carries no contractual cryptographic-strength
    // guarantee (the runtime happens to use a CSPRNG on current platforms, but the API doesn't
    // promise it), whereas RandomNumberGenerator is guaranteed CSPRNG. Same length/charset as Guid
    // "N", so it's a drop-in anywhere these round-trip as opaque strings.
    //
    // It lives in Core rather than in Server because the auth battery needs the same guarantee, and
    // no battery may reference Rask.Server — that is what keeps the meta-package free of cycles.
    public static string Create() => RandomNumberGenerator.GetHexString(32, true);
}
