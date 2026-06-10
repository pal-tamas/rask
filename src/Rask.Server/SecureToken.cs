using System.Security.Cryptography;

namespace Rask.Server;

internal static class SecureToken
{
    // A 128-bit cryptographically-random token as 32 lowercase hex chars. Used for values whose
    // secrecy IS the security boundary — redeem tickets (the authority to set the auth cookie) and
    // live-session ids (the bearer for the WS / upload / download endpoints). Replaces
    // Guid.NewGuid().ToString("N"): a v4 GUID exposes only ~122 random bits and carries no
    // contractual cryptographic-strength guarantee (the runtime happens to use a CSPRNG on current
    // platforms, but the API doesn't promise it), whereas RandomNumberGenerator is guaranteed CSPRNG.
    // Same length/charset as Guid "N", so it's a drop-in anywhere these round-trip as opaque strings.
    public static string Create() => RandomNumberGenerator.GetHexString(32, true);
}
