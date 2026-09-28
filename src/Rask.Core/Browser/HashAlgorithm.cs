namespace Rask.Core.Browser;

/// <summary>A hash algorithm supported by <c>crypto.subtle.digest</c>.</summary>
public enum HashAlgorithm
{
    /// <summary><c>SHA-1</c> — legacy; not collision-resistant. Avoid for security.</summary>
    Sha1,

    /// <summary><c>SHA-256</c>.</summary>
    Sha256,

    /// <summary><c>SHA-384</c>.</summary>
    Sha384,

    /// <summary><c>SHA-512</c>.</summary>
    Sha512
}
