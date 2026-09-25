namespace Rask.Auth;

/// <summary>Which algorithm new password hashes are made with.</summary>
/// <remarks>
/// Every stored format is still read whichever this is, and a sign-in whose hash is in another format, or under weaker
/// parameters, is rehashed. So changing it is safe on a live app: existing users move over as they sign in.
/// </remarks>
public enum PasswordHashing
{
    /// <summary>PBKDF2-HMAC-SHA256 at 600,000 iterations, from the base class library. The default.</summary>
    Pbkdf2,

    /// <summary>
    /// bcrypt, at <see cref="AuthOptions.BcryptWorkFactor" />. Reads the same <c>$2a$</c>/<c>$2b$</c>/<c>$2y$</c> hashes other
    /// frameworks write. bcrypt uses only the first 72 bytes of a password, so a longer one is refused rather than cut.
    /// </summary>
    Bcrypt,
}
