using System.Security.Cryptography;
using System.Text;

namespace Rask.Server.Diagnostics;

/// <summary>What the log calls a session: a fingerprint of its id, never the id — that is the secret a holder drives it with.</summary>
internal static class SessionFingerprint
{
    /// <summary>The first four bytes of the id's SHA-256, as eight hex digits.</summary>
    public static string Of(string sessionId) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(sessionId)).AsSpan(0, 4));
}
