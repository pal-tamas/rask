using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

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

/// <summary>What checking a password against a stored hash found.</summary>
internal enum PasswordCheck
{
    /// <summary>The password does not match, or the hash is not one this hasher reads.</summary>
    Failed,

    /// <summary>The password matches.</summary>
    Success,

    /// <summary>The password matches, and the hash should be replaced with one under the current settings.</summary>
    SuccessRehashNeeded,
}

/// <summary>
/// Hashes and checks passwords.
/// </summary>
/// <remarks>
/// <para>
/// Three formats are read: Rask's PBKDF2 (<c>$rask$pbkdf2-sha256$&lt;iterations&gt;$&lt;salt&gt;$&lt;hash&gt;</c>), bcrypt, and ASP.NET
/// Core Identity's V3 blob, so rows copied over from <c>AspNetUsers</c> keep working. New hashes use
/// <see cref="AuthOptions.PasswordHashing" />, and a match in any other format or under weaker parameters asks to be
/// rehashed.
/// </para>
/// <para>
/// PBKDF2 at 600,000 iterations is OWASP's recommendation and needs no dependency. bcrypt comes from BCrypt.Net-Next.
/// </para>
/// </remarks>
internal sealed class PasswordHasher
{
    /// <summary>The PBKDF2 work factor new hashes are made with.</summary>
    internal const int DefaultIterations = 600_000;

    /// <summary>bcrypt reads only this many bytes of a password.</summary>
    internal const int BcryptMaximumBytes = 72;

    private const string Pbkdf2Prefix = "$rask$pbkdf2-sha256$";
    private const int SaltBytes = 16;
    private const int HashBytes = 32;

    // Identity V3: 0x01 | prf (uint32 BE) | iterations (uint32 BE) | salt length (uint32 BE) | salt | subkey.
    private const byte IdentityV3Marker = 0x01;
    private const int IdentityV3HeaderBytes = 13;

    private readonly Lazy<string> _dummy;

    /// <summary>Creates a hasher.</summary>
    /// <param name="algorithm">What new hashes are made with.</param>
    /// <param name="bcryptWorkFactor">The bcrypt cost, when <paramref name="algorithm" /> is bcrypt.</param>
    /// <param name="iterations">The PBKDF2 iteration count. Tests use fewer.</param>
    internal PasswordHasher(
        PasswordHashing algorithm = PasswordHashing.Pbkdf2,
        int bcryptWorkFactor = AuthOptions.DefaultBcryptWorkFactor,
        int iterations = DefaultIterations)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(iterations, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(bcryptWorkFactor, 4);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(bcryptWorkFactor, 31);

        Algorithm = algorithm;
        BcryptWorkFactor = bcryptWorkFactor;
        Iterations = iterations;
        _dummy = new Lazy<string>(() => Hash(Convert.ToBase64String(RandomNumberGenerator.GetBytes(18))));
    }

    /// <summary>What new hashes are made with.</summary>
    internal PasswordHashing Algorithm { get; }

    /// <summary>The bcrypt cost new bcrypt hashes are made with.</summary>
    internal int BcryptWorkFactor { get; }

    /// <summary>The PBKDF2 iteration count new PBKDF2 hashes are made with.</summary>
    internal int Iterations { get; }

    /// <summary>Why <paramref name="password" /> cannot be hashed by the configured algorithm, or <see langword="null" />.</summary>
    /// <param name="password">The password.</param>
    /// <returns>A message for the person choosing it, or <see langword="null" /> when it is fine.</returns>
    public string? Refuse(string password) =>
        Algorithm == PasswordHashing.Bcrypt && Encoding.UTF8.GetByteCount(password) > BcryptMaximumBytes
            ? $"Passwords must be at most {BcryptMaximumBytes} bytes."
            : null;

    /// <summary>Hashes <paramref name="password" /> under a new random salt.</summary>
    /// <param name="password">The password.</param>
    /// <returns>The encoded hash.</returns>
    public string Hash(string password)
    {
        ArgumentNullException.ThrowIfNull(password);

        if (Algorithm == PasswordHashing.Bcrypt)
        {
            return BCrypt.Net.BCrypt.HashPassword(password, BcryptWorkFactor);
        }

        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashBytes);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{Pbkdf2Prefix}{Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}");
    }

    /// <summary>Checks <paramref name="password" /> against <paramref name="encoded" />.</summary>
    /// <param name="encoded">The stored hash.</param>
    /// <param name="password">The password that was typed.</param>
    /// <returns>Whether it matched, and whether the hash should be renewed.</returns>
    public PasswordCheck Verify(string? encoded, string password)
    {
        ArgumentNullException.ThrowIfNull(password);

        if (string.IsNullOrEmpty(encoded))
        {
            return PasswordCheck.Failed;
        }

        if (encoded.StartsWith(Pbkdf2Prefix, StringComparison.Ordinal))
        {
            return VerifyPbkdf2(encoded, password);
        }

        return IsBcrypt(encoded) ? VerifyBcrypt(encoded, password) : VerifyIdentityV3(encoded, password);
    }

    /// <summary>
    /// Spends the same work as a real check, against nothing. Called for an address with no account, so answering takes
    /// as long as it does for one that has.
    /// </summary>
    /// <param name="password">The password that was typed.</param>
    public void VerifyNothing(string password) => _ = Verify(_dummy.Value, password);

    private PasswordCheck VerifyPbkdf2(string encoded, string password)
    {
        var parts = encoded[Pbkdf2Prefix.Length..].Split('$');
        if (parts.Length != 3
            || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var iterations)
            || iterations < 1)
        {
            return PasswordCheck.Failed;
        }

        byte[] salt;
        byte[] expected;
        try
        {
            salt = Convert.FromBase64String(parts[1]);
            expected = Convert.FromBase64String(parts[2]);
        }
        catch (FormatException)
        {
            return PasswordCheck.Failed;
        }

        if (expected.Length == 0)
        {
            return PasswordCheck.Failed;
        }

        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);

        if (!CryptographicOperations.FixedTimeEquals(actual, expected))
        {
            return PasswordCheck.Failed;
        }

        return Algorithm != PasswordHashing.Pbkdf2
               || iterations < Iterations
               || expected.Length != HashBytes
               || salt.Length != SaltBytes
            ? PasswordCheck.SuccessRehashNeeded
            : PasswordCheck.Success;
    }

    private PasswordCheck VerifyBcrypt(string encoded, string password)
    {
        bool matched;
        try
        {
            matched = BCrypt.Net.BCrypt.Verify(password, encoded);
        }
        catch (Exception exception) when (
            exception is BCrypt.Net.SaltParseException or BCrypt.Net.BcryptAuthenticationException
                or BCrypt.Net.HashInformationException or ArgumentException or FormatException)
        {
            // A stored value that looks like bcrypt but is not parses no further. Checking a password is not the
            // place to throw: it fails closed, like every other unreadable hash.
            return PasswordCheck.Failed;
        }

        if (!matched)
        {
            return PasswordCheck.Failed;
        }

        return Algorithm != PasswordHashing.Bcrypt || BcryptCost(encoded) < BcryptWorkFactor
            ? PasswordCheck.SuccessRehashNeeded
            : PasswordCheck.Success;
    }

    private static PasswordCheck VerifyIdentityV3(string encoded, string password)
    {
        byte[] blob;
        try
        {
            blob = Convert.FromBase64String(encoded);
        }
        catch (FormatException)
        {
            return PasswordCheck.Failed;
        }

        if (blob.Length < IdentityV3HeaderBytes || blob[0] != IdentityV3Marker)
        {
            return PasswordCheck.Failed;
        }

        var prf = BinaryPrimitives.ReadUInt32BigEndian(blob.AsSpan(1));
        var iterations = BinaryPrimitives.ReadUInt32BigEndian(blob.AsSpan(5));
        var saltLength = BinaryPrimitives.ReadUInt32BigEndian(blob.AsSpan(9));

        HashAlgorithmName? algorithm = prf switch
        {
            0 => HashAlgorithmName.SHA1,
            1 => HashAlgorithmName.SHA256,
            2 => HashAlgorithmName.SHA512,
            _ => null,
        };

        if (algorithm is not { } name
            || iterations is 0 or > int.MaxValue
            || saltLength < 8
            || IdentityV3HeaderBytes + (long)saltLength >= blob.Length)
        {
            return PasswordCheck.Failed;
        }

        var salt = blob.AsSpan(IdentityV3HeaderBytes, (int)saltLength).ToArray();
        var expected = blob.AsSpan(IdentityV3HeaderBytes + (int)saltLength);
        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, (int)iterations, name, expected.Length);

        // A match is always rehashed: Identity's format is read so existing rows keep working, never written.
        return CryptographicOperations.FixedTimeEquals(actual, expected)
            ? PasswordCheck.SuccessRehashNeeded
            : PasswordCheck.Failed;
    }

    // $2a$10$…, $2b$12$…, $2y$12$… — the cost is the two digits after the second '$'.
    private static bool IsBcrypt(string encoded) =>
        encoded.Length == 60
        && encoded[0] == '$'
        && encoded[1] == '2'
        && encoded[3] == '$'
        && encoded[6] == '$';

    private static int BcryptCost(string encoded) =>
        int.TryParse(encoded.AsSpan(4, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var cost) ? cost : 0;
}
