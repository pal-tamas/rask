using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Rask.Auth.Tests;

/// <summary>Hashing and checking passwords, in every format the hasher reads.</summary>
/// <remarks>No database, so deliberately not in the database collection.</remarks>
public sealed class PasswordHasherTests
{
    private const string Password = "correct horse battery staple";

    private static readonly PasswordHasher Pbkdf2 = new(iterations: 1_000);
    private static readonly PasswordHasher Bcrypt = new(PasswordHashing.Bcrypt, bcryptWorkFactor: 4, iterations: 1_000);

    [Fact]
    public void A_pbkdf2_hash_checks_its_own_password_and_no_other()
    {
        var hash = Pbkdf2.Hash(Password);

        Assert.StartsWith("$rask$pbkdf2-sha256$1000$", hash, StringComparison.Ordinal);
        Assert.Equal(PasswordCheck.Success, Pbkdf2.Verify(hash, Password));
        Assert.Equal(PasswordCheck.Failed, Pbkdf2.Verify(hash, Password + "!"));
    }

    [Fact]
    public void Two_hashes_of_one_password_differ_because_the_salt_does()
    {
        Assert.NotEqual(Pbkdf2.Hash(Password), Pbkdf2.Hash(Password));
    }

    [Fact]
    public void A_hash_made_with_fewer_iterations_than_configured_asks_to_be_renewed()
    {
        var weaker = new PasswordHasher(iterations: 500).Hash(Password);

        Assert.Equal(PasswordCheck.SuccessRehashNeeded, Pbkdf2.Verify(weaker, Password));
    }

    [Fact]
    public void A_bcrypt_hash_checks_its_own_password_and_no_other()
    {
        var hash = Bcrypt.Hash(Password);

        Assert.StartsWith("$2", hash, StringComparison.Ordinal);
        Assert.Equal(PasswordCheck.Success, Bcrypt.Verify(hash, Password));
        Assert.Equal(PasswordCheck.Failed, Bcrypt.Verify(hash, Password + "!"));
    }

    [Fact]
    public void A_bcrypt_hash_written_by_another_framework_is_read()
    {
        // $2y$ is what PHP's password_hash writes; $2a$ is the older revision Ruby's bcrypt gem writes.
        foreach (var revision in new[] { 'y', 'a' })
        {
            var foreign = BCrypt.Net.BCrypt.HashPassword(Password, BCrypt.Net.BCrypt.GenerateSalt(4, revision));

            Assert.Equal(PasswordCheck.Success, Bcrypt.Verify(foreign, Password));
        }
    }

    [Fact]
    public void Switching_the_algorithm_renews_every_hash_in_the_other_format_on_its_next_check()
    {
        Assert.Equal(PasswordCheck.SuccessRehashNeeded, Bcrypt.Verify(Pbkdf2.Hash(Password), Password));
        Assert.Equal(PasswordCheck.SuccessRehashNeeded, Pbkdf2.Verify(Bcrypt.Hash(Password), Password));
    }

    [Fact]
    public void A_bcrypt_hash_under_a_lower_cost_than_configured_asks_to_be_renewed()
    {
        var cheaper = BCrypt.Net.BCrypt.HashPassword(Password, 4);
        var configured = new PasswordHasher(PasswordHashing.Bcrypt, bcryptWorkFactor: 5, iterations: 1_000);

        Assert.Equal(PasswordCheck.SuccessRehashNeeded, configured.Verify(cheaper, Password));
    }

    [Theory]
    [InlineData(2, 100_000)] // HMAC-SHA512, what Identity writes today
    [InlineData(1, 10_000)] // HMAC-SHA256, what older Identity versions wrote
    public void An_identity_v3_hash_is_read_and_always_renewed(uint prf, int iterations)
    {
        var hash = IdentityV3(Password, prf, iterations);

        Assert.Equal(PasswordCheck.SuccessRehashNeeded, Pbkdf2.Verify(hash, Password));
        Assert.Equal(PasswordCheck.Failed, Pbkdf2.Verify(hash, "wrong"));
    }

    [Fact]
    public void Bcrypt_refuses_a_password_it_would_silently_cut_short()
    {
        Assert.Null(Bcrypt.Refuse(new string('a', 72)));
        Assert.NotNull(Bcrypt.Refuse(new string('a', 73)));

        // Bytes, not characters: 40 two-byte characters are 80 bytes.
        Assert.NotNull(Bcrypt.Refuse(new string('é', 40)));

        Assert.Null(Pbkdf2.Refuse(new string('a', 200)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a hash")]
    [InlineData("$rask$pbkdf2-sha256$x$y$z")]
    [InlineData("$rask$pbkdf2-sha256$1000$!!!$!!!")]
    public void Anything_that_is_not_a_readable_hash_fails_rather_than_throwing(string? stored)
    {
        Assert.Equal(PasswordCheck.Failed, Pbkdf2.Verify(stored, Password));
    }

    [Fact]
    public void Checking_against_nothing_spends_the_work_and_matches_nothing()
    {
        Pbkdf2.VerifyNothing(Password);
        Bcrypt.VerifyNothing(Password);
    }

    // Identity's V3 layout, written independently of the hasher under test: 0x01, the PRF, the iteration count and the
    // salt length (all big-endian), then the salt and the subkey.
    private static string IdentityV3(string password, uint prf, int iterations)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var algorithm = prf == 2 ? HashAlgorithmName.SHA512 : HashAlgorithmName.SHA256;
        var subkey = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, algorithm, 32);

        var blob = new byte[13 + salt.Length + subkey.Length];
        blob[0] = 0x01;
        BinaryPrimitives.WriteUInt32BigEndian(blob.AsSpan(1), prf);
        BinaryPrimitives.WriteUInt32BigEndian(blob.AsSpan(5), (uint)iterations);
        BinaryPrimitives.WriteUInt32BigEndian(blob.AsSpan(9), (uint)salt.Length);
        salt.CopyTo(blob, 13);
        subkey.CopyTo(blob, 13 + salt.Length);

        return Convert.ToBase64String(blob);
    }
}
