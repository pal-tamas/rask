namespace Rask.Data;

/// <summary>
///     What a <see cref="Rask.Values.Secret{T}" /> column is encrypted with. Rask.Server registers one over the host's
///     ASP.NET Core Data Protection key ring, the ring a deploy already persists; register your own only to keep the
///     keys elsewhere.
/// </summary>
/// <remarks>
///     A ciphertext must be readable for as long as the row lives, so the keys behind it must be kept as long as the
///     data: losing the key ring loses every secret stored with it.
/// </remarks>
public interface ISecrets
{
    /// <summary>The text to store in place of <paramref name="plaintext" />.</summary>
    /// <param name="plaintext">What the secret holds.</param>
    /// <returns>Its ciphertext.</returns>
    string Protect(string plaintext);

    /// <summary>What <paramref name="stored" /> holds.</summary>
    /// <param name="stored">A ciphertext <see cref="Protect" /> made.</param>
    /// <returns>The plaintext.</returns>
    /// <exception cref="System.Security.Cryptography.CryptographicException">The ciphertext cannot be read with these keys.</exception>
    string Unprotect(string stored);
}
