using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Rask.Wire;

namespace Rask.Auth.Tests;

/// <summary>
/// The WebAuthn verifier: what it accepts, and — mostly — what it refuses.
/// </summary>
/// <remarks>
/// No database and no host: this is the cryptography and the parsing on their own, against structures a
/// <see cref="TestAuthenticator" /> assembles the way a real authenticator does. Every refusal below is a way into
/// somebody's account if it were ever accepted, which is why there are more of them than acceptances.
/// </remarks>
public sealed class PasskeyVerifierTests
{
    private const string RelyingPartyId = "rask.test";
    private const string Origin = "https://rask.test";

    private static readonly byte[] Challenge = RandomNumberGenerator.GetBytes(32);

    [Fact]
    public void An_es256_registration_verifies_and_yields_the_credential()
    {
        using var authenticator = new TestAuthenticator { SignCount = 3, BackedUp = true };

        var verified = PasskeyVerifier.VerifyRegistration(
            authenticator.Register(RelyingPartyId, Origin, Challenge), Ceremony(), out var failure);

        Assert.Equal("", failure);
        Assert.NotNull(verified);
        Assert.Equal(authenticator.CredentialId, verified.Value.CredentialId);
        Assert.Equal(-7, verified.Value.Algorithm);
        Assert.Equal(3u, verified.Value.SignCount);
        Assert.True(verified.Value.BackedUp);

        // The stored key is a COSE key that can be read back, which is what the sign-in path will do.
        Assert.NotEmpty(verified.Value.PublicKey);
    }

    [Fact]
    public void An_rs256_registration_verifies_too()
    {
        using var authenticator = new TestAuthenticator(rsa: true);

        var verified = PasskeyVerifier.VerifyRegistration(
            authenticator.Register(RelyingPartyId, Origin, Challenge), Ceremony(), out _);

        Assert.NotNull(verified);
        Assert.Equal(-257, verified.Value.Algorithm);
    }

    [Fact]
    public void A_signature_from_the_registered_key_verifies()
    {
        using var authenticator = new TestAuthenticator();
        var passkey = Registered(authenticator);
        authenticator.SignCount = 1;

        var verified = PasskeyVerifier.VerifyAssertion(
            authenticator.SignIn(RelyingPartyId, Origin, Challenge, passkey.UserId), Ceremony(), passkey, out var failure);

        Assert.Equal("", failure);
        Assert.NotNull(verified);
        Assert.Equal(1u, verified.Value.SignCount);
    }

    [Fact]
    public void An_rs256_signature_verifies_too()
    {
        using var authenticator = new TestAuthenticator(rsa: true);
        var passkey = Registered(authenticator);
        authenticator.SignCount = 1;

        Assert.NotNull(PasskeyVerifier.VerifyAssertion(
            authenticator.SignIn(RelyingPartyId, Origin, Challenge, passkey.UserId), Ceremony(), passkey, out _));
    }

    /// <summary>A synced passkey keeps no counter and reports zero forever, which is not a clone.</summary>
    [Fact]
    public void A_counter_that_stays_at_zero_is_accepted_every_time()
    {
        using var authenticator = new TestAuthenticator { BackedUp = true };
        var passkey = Registered(authenticator);

        for (var i = 0; i < 3; i++)
        {
            var verified = PasskeyVerifier.VerifyAssertion(
                authenticator.SignIn(RelyingPartyId, Origin, Challenge, passkey.UserId), Ceremony(), passkey, out _);

            Assert.NotNull(verified);
            passkey.Used(verified.Value.SignCount, DateTime.UtcNow);
        }
    }

    /// <summary>A counter that fails to advance means two authenticators answer for one credential.</summary>
    [Fact]
    public void A_counter_that_does_not_advance_is_refused_as_a_clone()
    {
        using var authenticator = new TestAuthenticator { SignCount = 7 };
        var passkey = Registered(authenticator);
        passkey.Used(7, DateTime.UtcNow);

        Assert.Null(PasskeyVerifier.VerifyAssertion(
            authenticator.SignIn(RelyingPartyId, Origin, Challenge, passkey.UserId), Ceremony(), passkey, out var failure));
        Assert.Contains("counter", failure, StringComparison.Ordinal);
    }

    [Fact]
    public void A_challenge_this_server_did_not_issue_is_refused()
    {
        using var authenticator = new TestAuthenticator();

        Assert.Null(PasskeyVerifier.VerifyRegistration(
            authenticator.Register(RelyingPartyId, Origin, RandomNumberGenerator.GetBytes(32)),
            Ceremony(),
            out var failure));
        Assert.Contains("challenge", failure, StringComparison.Ordinal);
    }

    [Fact]
    public void A_ceremony_from_another_origin_is_refused()
    {
        using var authenticator = new TestAuthenticator();

        Assert.Null(PasskeyVerifier.VerifyRegistration(
            authenticator.Register(RelyingPartyId, "https://evil.test", Challenge), Ceremony(), out var failure));
        Assert.Contains("origin", failure, StringComparison.Ordinal);
    }

    /// <summary>The check that makes passkeys unphishable: a key for another site cannot answer for this one.</summary>
    [Fact]
    public void A_credential_bound_to_another_relying_party_is_refused()
    {
        using var authenticator = new TestAuthenticator();

        Assert.Null(PasskeyVerifier.VerifyRegistration(
            authenticator.Register("evil.test", Origin, Challenge), Ceremony(), out var failure));
        Assert.Contains("relying party", failure, StringComparison.Ordinal);
    }

    [Fact]
    public void An_assertion_replayed_into_a_registration_is_refused()
    {
        using var authenticator = new TestAuthenticator();
        var assertion = authenticator.SignIn(RelyingPartyId, Origin, Challenge);

        // The client data says "webauthn.get". Accepting it would register a key the user never created.
        var registration = new PasskeyRegistrationRequest(
            "state", null, assertion.RawId, assertion.ClientDataJson, assertion.AuthenticatorData);

        Assert.Null(PasskeyVerifier.VerifyRegistration(registration, Ceremony(), out var failure));
        Assert.Contains("ceremony", failure, StringComparison.Ordinal);
    }

    [Fact]
    public void A_ceremony_run_in_someone_elses_frame_is_refused()
    {
        using var authenticator = new TestAuthenticator();
        var credential = authenticator.Register(RelyingPartyId, Origin, Challenge);

        var crossOrigin = Encoding.UTF8.GetString(
            Base64Url.DecodeFromChars(credential.ClientDataJson)).Replace(
            "\"crossOrigin\":false", "\"crossOrigin\":true", StringComparison.Ordinal);

        Assert.Null(PasskeyVerifier.VerifyRegistration(
            credential with { ClientDataJson = Base64Url.EncodeToString(Encoding.UTF8.GetBytes(crossOrigin)) },
            Ceremony(),
            out var failure));
        Assert.Contains("cross-origin", failure, StringComparison.Ordinal);
    }

    [Fact]
    public void An_authenticator_that_reports_nobody_present_is_refused()
    {
        using var authenticator = new TestAuthenticator { UserPresent = false };

        Assert.Null(PasskeyVerifier.VerifyRegistration(
            authenticator.Register(RelyingPartyId, Origin, Challenge), Ceremony(), out var failure));
        Assert.Contains("present", failure, StringComparison.Ordinal);
    }

    /// <summary>Rask always asks for user verification, so a passkey is the device AND the biometric.</summary>
    [Fact]
    public void An_authenticator_that_skipped_user_verification_is_refused()
    {
        using var authenticator = new TestAuthenticator { UserVerified = false };

        Assert.Null(PasskeyVerifier.VerifyRegistration(
            authenticator.Register(RelyingPartyId, Origin, Challenge), Ceremony(), out var failure));
        Assert.Contains("verified", failure, StringComparison.Ordinal);
    }

    [Fact]
    public void A_tampered_signature_is_refused()
    {
        using var authenticator = new TestAuthenticator();
        var passkey = Registered(authenticator);
        authenticator.SignCount = 1;

        var assertion = authenticator.SignIn(RelyingPartyId, Origin, Challenge, passkey.UserId);
        var signature = Base64Url.DecodeFromChars(assertion.Signature);
        signature[^1] ^= 0xFF;

        Assert.Null(PasskeyVerifier.VerifyAssertion(
            assertion with { Signature = Base64Url.EncodeToString(signature) }, Ceremony(), passkey, out var failure));
        Assert.Contains("signature", failure, StringComparison.Ordinal);
    }

    /// <summary>One person's authenticator must not be able to answer for another person's credential.</summary>
    [Fact]
    public void A_signature_from_a_different_key_is_refused()
    {
        using var mine = new TestAuthenticator();
        using var theirs = new TestAuthenticator(credentialId: mine.CredentialId);

        var passkey = Registered(mine);
        theirs.SignCount = 1;

        Assert.Null(PasskeyVerifier.VerifyAssertion(
            theirs.SignIn(RelyingPartyId, Origin, Challenge, passkey.UserId), Ceremony(), passkey, out var failure));
        Assert.Contains("signature", failure, StringComparison.Ordinal);
    }

    [Fact]
    public void A_user_handle_for_somebody_else_is_refused()
    {
        using var authenticator = new TestAuthenticator();
        var passkey = Registered(authenticator);
        authenticator.SignCount = 1;

        Assert.Null(PasskeyVerifier.VerifyAssertion(
            authenticator.SignIn(RelyingPartyId, Origin, Challenge, Guid.NewGuid()), Ceremony(), passkey, out var failure));
        Assert.Contains("user handle", failure, StringComparison.Ordinal);
    }

    [Fact]
    public void A_credential_id_that_is_not_the_stored_one_is_refused()
    {
        using var authenticator = new TestAuthenticator();
        var passkey = Registered(authenticator);
        authenticator.SignCount = 1;

        var assertion = authenticator.SignIn(RelyingPartyId, Origin, Challenge, passkey.UserId);

        Assert.Null(PasskeyVerifier.VerifyAssertion(
            assertion with { RawId = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32)) },
            Ceremony(),
            passkey,
            out var failure));
        Assert.Contains("credential id", failure, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not base64url!!")]
    [InlineData("AAAA")]
    public void Garbage_is_refused_rather_than_thrown(string field)
    {
        using var authenticator = new TestAuthenticator();
        var credential = authenticator.Register(RelyingPartyId, Origin, Challenge);

        Assert.Null(PasskeyVerifier.VerifyRegistration(
            credential with { AttestationObject = field }, Ceremony(), out var attestation));
        Assert.NotEqual("", attestation);

        Assert.Null(PasskeyVerifier.VerifyRegistration(
            credential with { ClientDataJson = field }, Ceremony(), out var clientData));
        Assert.NotEqual("", clientData);
    }

    /// <summary>Authenticator data cut short must not be read past the end of.</summary>
    [Fact]
    public void Truncated_authenticator_data_is_refused()
    {
        using var authenticator = new TestAuthenticator();
        var passkey = Registered(authenticator);
        var assertion = authenticator.SignIn(RelyingPartyId, Origin, Challenge, passkey.UserId);
        var authData = Base64Url.DecodeFromChars(assertion.AuthenticatorData);

        Assert.Null(PasskeyVerifier.VerifyAssertion(
            assertion with { AuthenticatorData = Base64Url.EncodeToString(authData.AsSpan(0, 20)) },
            Ceremony(),
            passkey,
            out var failure));
        Assert.Contains("short", failure, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unsupported_algorithm_is_refused_at_registration()
    {
        // A key the verifier will not read is not a credential it can ever check a signature against, so it is
        // refused when it arrives rather than stored and discovered at the first sign-in.
        using var ed25519 = new TestAuthenticator { Unsupported = true };

        Assert.Null(PasskeyVerifier.VerifyRegistration(
            ed25519.Register(RelyingPartyId, Origin, Challenge), Ceremony(), out var failure));
        Assert.Contains("public key", failure, StringComparison.Ordinal);
    }

    /// <summary>
    /// With no origin configured — a development app, or a component handler that cannot see a request — the
    /// relying party id is what a ceremony is held to, which is the binding that actually protects the account.
    /// </summary>
    [Theory]
    [InlineData("https://rask.test", true)]
    [InlineData("https://rask.test:5001", true)]
    [InlineData("https://app.rask.test", true)]
    [InlineData("https://rask.test.evil.com", false)]
    [InlineData("https://notrask.test", false)]
    [InlineData("https://evil.test", false)]
    public void Without_a_configured_origin_the_relying_party_id_is_what_a_ceremony_is_held_to(
        string origin, bool allowed)
    {
        using var authenticator = new TestAuthenticator();
        var open = new PasskeyCeremony(RelyingPartyId, [], Challenge);

        var verified = PasskeyVerifier.VerifyRegistration(
            authenticator.Register(RelyingPartyId, origin, Challenge), open, out _);

        Assert.Equal(allowed, verified is not null);
    }

    /// <summary>A configured list is exact: a subdomain that is not on it is not served by this app.</summary>
    [Fact]
    public void A_configured_origin_list_is_exact()
    {
        using var authenticator = new TestAuthenticator();

        Assert.Null(PasskeyVerifier.VerifyRegistration(
            authenticator.Register(RelyingPartyId, "https://app.rask.test", Challenge), Ceremony(), out _));
    }

    private static PasskeyCeremony Ceremony() => new(RelyingPartyId, [Origin], Challenge);

    private static Passkey Registered(TestAuthenticator authenticator)
    {
        var verified = PasskeyVerifier.VerifyRegistration(
            authenticator.Register(RelyingPartyId, Origin, Challenge), Ceremony(), out _);

        Assert.NotNull(verified);

        return Passkey.Register(
            Guid.NewGuid(),
            "Test",
            verified.Value.CredentialId,
            verified.Value.PublicKey,
            verified.Value.Algorithm,
            verified.Value.SignCount,
            verified.Value.BackedUp,
            "internal",
            DateTime.UtcNow);
    }
}
