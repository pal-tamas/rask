using System.Buffers.Binary;
using System.Formats.Cbor;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Rask.Wire;

namespace Rask.Auth;

/// <summary>What the browser must prove against: the relying party, the origins it may answer from, and the challenge.</summary>
/// <param name="RelyingPartyId">The RP id the authenticator hashed — a registrable domain, no scheme and no port.</param>
/// <param name="Origins">The origins a ceremony may come from, compared exactly.</param>
/// <param name="Challenge">The random challenge this ceremony was started with.</param>
internal sealed record PasskeyCeremony(string RelyingPartyId, IReadOnlyList<string> Origins, byte[] Challenge);

/// <summary>What a verified registration yields: everything needed to store the credential.</summary>
internal readonly record struct PasskeyRegistration(
    byte[] CredentialId, byte[] PublicKey, int Algorithm, uint SignCount, bool BackedUp);

/// <summary>What a verified assertion yields: the authenticator's new signature counter.</summary>
internal readonly record struct PasskeyAssertion(uint SignCount);

/// <summary>
/// The relying party's half of WebAuthn, on the BCL: verifies what a browser returns from a passkey ceremony.
/// </summary>
/// <remarks>
/// <para>
/// WebAuthn Level 2 §7.1 (registration) and §7.2 (authentication), with the attestation statement deliberately not
/// verified — that is attestation <c>none</c>, which is what a site wants unless it must prove <em>which model</em> of
/// authenticator a user holds. Everything that actually protects the account is checked: the challenge is the one this
/// server issued, the origin is one of ours, the RP id hash matches, the user was present and verified, and the
/// signature is the stored key's over exactly the bytes the authenticator signed.
/// </para>
/// <para>
/// Every step fails closed and says why only to the log. The caller turns any failure into the same
/// <c>InvalidCredentials</c> the wrong password gets, so a prober learns nothing from which check failed.
/// </para>
/// </remarks>
internal static class PasskeyVerifier
{
    private const string RegistrationType = "webauthn.create";
    private const string AssertionType = "webauthn.get";

    private const int HashLength = 32;
    private const int FlagsOffset = HashLength;
    private const int SignCountOffset = FlagsOffset + 1;

    /// <summary>rpIdHash (32) + flags (1) + signCount (4).</summary>
    private const int AuthenticatorDataHeaderLength = SignCountOffset + 4;

    private const int AaguidLength = 16;

    /// <summary>The longest credential id WebAuthn allows.</summary>
    private const int MaxCredentialIdLength = 1023;

    /// <summary>A ceiling on every base64url field, so a hostile body cannot ask for a large allocation.</summary>
    private const int MaxFieldLength = 64 * 1024;

    /// <summary>Verifies a registration ceremony, returning the credential to store, or <see langword="null" />.</summary>
    /// <param name="credential">What <c>navigator.credentials.create</c> returned, as it crossed the wire.</param>
    /// <param name="ceremony">The challenge and relying party this ceremony was started with.</param>
    /// <param name="failure">Why it failed, for the log. Never shown to the caller.</param>
    public static PasskeyRegistration? VerifyRegistration(
        PasskeyRegistrationRequest credential, PasskeyCeremony ceremony, out string failure)
    {
        ArgumentNullException.ThrowIfNull(credential);
        ArgumentNullException.ThrowIfNull(ceremony);

        if (!TryDecode(credential.ClientDataJson, out var clientData)
            || !TryDecode(credential.AttestationObject, out var attestationObject)
            || !TryDecode(credential.RawId, out var rawId))
        {
            failure = "a credential field was not base64url";
            return null;
        }

        if (!IsExpectedClientData(clientData, RegistrationType, ceremony, out failure)
            || !TryReadAttestation(attestationObject, out var authData, out failure)
            || !IsExpectedAuthenticatorData(authData, ceremony, out var flags, out var signCount, out failure))
        {
            return null;
        }

        if ((flags & AuthenticatorFlags.AttestedCredentialData) == 0)
        {
            failure = "the authenticator returned no credential";
            return null;
        }

        if (!TryReadAttestedCredential(authData, flags, out var credentialId, out var publicKey, out var algorithm, out failure))
        {
            return null;
        }

        if (!CryptographicOperations.FixedTimeEquals(credentialId, rawId))
        {
            // The id the authenticator signed and the id the browser reported have to be the same credential.
            failure = "the credential id does not match the signed one";
            return null;
        }

        failure = "";
        return new PasskeyRegistration(
            credentialId, publicKey, algorithm, signCount, (flags & AuthenticatorFlags.BackedUp) != 0);
    }

    /// <summary>Verifies an assertion against a stored passkey, returning its new counter, or <see langword="null" />.</summary>
    /// <param name="credential">What <c>navigator.credentials.get</c> returned, as it crossed the wire.</param>
    /// <param name="ceremony">The challenge and relying party this ceremony was started with.</param>
    /// <param name="passkey">The stored passkey the credential id resolved to.</param>
    /// <param name="failure">Why it failed, for the log. Never shown to the caller.</param>
    public static PasskeyAssertion? VerifyAssertion(
        PasskeyLoginRequest credential, PasskeyCeremony ceremony, Passkey passkey, out string failure)
    {
        ArgumentNullException.ThrowIfNull(credential);
        ArgumentNullException.ThrowIfNull(ceremony);
        ArgumentNullException.ThrowIfNull(passkey);

        if (!TryDecode(credential.ClientDataJson, out var clientData)
            || !TryDecode(credential.AuthenticatorData, out var authData)
            || !TryDecode(credential.Signature, out var signature)
            || !TryDecode(credential.RawId, out var rawId))
        {
            failure = "a credential field was not base64url";
            return null;
        }

        if (!CryptographicOperations.FixedTimeEquals(rawId, passkey.CredentialId))
        {
            failure = "the credential id does not match the stored passkey";
            return null;
        }

        if (!IsExpectedUserHandle(credential.UserHandle, passkey.UserId, out failure)
            || !IsExpectedClientData(clientData, AssertionType, ceremony, out failure)
            || !IsExpectedAuthenticatorData(authData, ceremony, out _, out var signCount, out failure))
        {
            return null;
        }

        // The authenticator signed its own data followed by the hash of what the browser told it — nothing else, and
        // nothing this server chose after the fact.
        var signed = new byte[authData.Length + HashLength];
        authData.CopyTo(signed, 0);
        SHA256.HashData(clientData, signed.AsSpan(authData.Length));

        using var key = CoseKey.Read(passkey.PublicKey, out _);
        if (key is null || !key.Verify(signed, signature))
        {
            failure = "the signature did not verify";
            return null;
        }

        // A counter that fails to move means two authenticators answer for one credential. Synced passkeys keep no
        // counter at all and report zero forever, which is why zero on both sides is not a regression.
        if ((signCount != 0 || passkey.SignCount != 0) && signCount <= passkey.SignCount)
        {
            failure = "the signature counter did not advance";
            return null;
        }

        failure = "";
        return new PasskeyAssertion(signCount);
    }

    private static bool IsExpectedUserHandle(string? userHandle, Guid userId, out string failure)
    {
        failure = "";

        if (string.IsNullOrEmpty(userHandle))
        {
            // Optional on a non-discoverable assertion; the credential id has already identified the account.
            return true;
        }

        if (!TryDecode(userHandle, out var handle) || handle.Length != 16 || new Guid(handle) != userId)
        {
            failure = "the user handle does not match the passkey's owner";
            return false;
        }

        return true;
    }

    private static bool IsExpectedClientData(
        byte[] clientData, string expectedType, PasskeyCeremony ceremony, out string failure)
    {
        failure = "";

        try
        {
            using var document = JsonDocument.Parse(clientData);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                failure = "client data was not an object";
                return false;
            }

            if (!root.TryGetProperty("type", out var type)
                || type.ValueKind != JsonValueKind.String
                || !string.Equals(type.GetString(), expectedType, StringComparison.Ordinal))
            {
                // A "webauthn.get" reply to a registration would otherwise register a key the user never made.
                failure = "client data was for a different ceremony";
                return false;
            }

            if (!root.TryGetProperty("challenge", out var challenge)
                || challenge.ValueKind != JsonValueKind.String
                || !TryDecode(challenge.GetString(), out var issued)
                || !CryptographicOperations.FixedTimeEquals(issued, ceremony.Challenge))
            {
                failure = "the challenge was not the one issued";
                return false;
            }

            if (!root.TryGetProperty("origin", out var origin)
                || origin.ValueKind != JsonValueKind.String
                || origin.GetString() is not { } value
                || !IsAllowedOrigin(value, ceremony))
            {
                failure = "the ceremony came from an origin this app does not serve";
                return false;
            }

            if (root.TryGetProperty("crossOrigin", out var crossOrigin) && crossOrigin.ValueKind == JsonValueKind.True)
            {
                // A ceremony run inside someone else's frame is not this user acting on this site.
                failure = "the ceremony ran cross-origin";
                return false;
            }

            return true;
        }
        catch (JsonException)
        {
            failure = "client data was not JSON";
            return false;
        }
    }

    /// <summary>Whether a ceremony may come from <paramref name="origin" />.</summary>
    /// <remarks>
    /// A configured list is exact and nothing else is allowed. With no list — an app that configured neither
    /// <c>PasskeyOrigins</c> nor <c>PublicOrigin</c>, which is the development case and the case where a component
    /// handler on a socket cannot see a request — the origin's host must be the relying party id or a subdomain of
    /// it. That is WebAuthn's own rule, and it is the binding that matters: the authenticator will not sign for this
    /// relying party anywhere else, whatever port the app happens to be served on.
    /// </remarks>
    private static bool IsAllowedOrigin(string origin, PasskeyCeremony ceremony)
    {
        if (ceremony.Origins.Count > 0)
        {
            return ceremony.Origins.Contains(origin, StringComparer.Ordinal);
        }

        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var host = uri.Host;
        var relyingParty = ceremony.RelyingPartyId;

        return string.Equals(host, relyingParty, StringComparison.OrdinalIgnoreCase)
               || (host.Length > relyingParty.Length
                   && host.EndsWith(relyingParty, StringComparison.OrdinalIgnoreCase)
                   && host[host.Length - relyingParty.Length - 1] == '.');
    }

    private static bool IsExpectedAuthenticatorData(
        byte[] authData, PasskeyCeremony ceremony, out AuthenticatorFlags flags, out uint signCount, out string failure)
    {
        flags = default;
        signCount = 0;

        if (authData.Length < AuthenticatorDataHeaderLength)
        {
            failure = "authenticator data was too short";
            return false;
        }

        Span<byte> expected = stackalloc byte[HashLength];
        SHA256.HashData(Encoding.UTF8.GetBytes(ceremony.RelyingPartyId), expected);

        if (!CryptographicOperations.FixedTimeEquals(authData.AsSpan(0, HashLength), expected))
        {
            // The authenticator answered a different site. This is the check that makes passkeys unphishable.
            failure = "the relying party id did not match";
            return false;
        }

        flags = (AuthenticatorFlags)authData[FlagsOffset];
        signCount = BinaryPrimitives.ReadUInt32BigEndian(authData.AsSpan(SignCountOffset, 4));

        if ((flags & AuthenticatorFlags.UserPresent) == 0)
        {
            failure = "the user was not present";
            return false;
        }

        if ((flags & AuthenticatorFlags.UserVerified) == 0)
        {
            // Rask always asks for user verification, so a passkey is two factors: the device, and the biometric or PIN
            // that unlocked it. An authenticator that skipped it is not what was asked for.
            failure = "the user was not verified";
            return false;
        }

        failure = "";
        return true;
    }

    private static bool TryReadAttestation(byte[] attestationObject, out byte[] authData, out string failure)
    {
        authData = [];

        try
        {
            var reader = new CborReader(attestationObject, CborConformanceMode.Strict);

            if (reader.PeekState() != CborReaderState.StartMap || reader.ReadStartMap() is not { } count)
            {
                failure = "the attestation object was not a definite-length map";
                return false;
            }

            byte[]? found = null;

            for (var i = 0; i < count; i++)
            {
                if (reader.PeekState() != CborReaderState.TextString)
                {
                    failure = "the attestation object had a non-text key";
                    return false;
                }

                // fmt and attStmt are read past on purpose: not verifying the attestation statement IS attestation
                // "none", which is what a site that does not care which model of authenticator a user owns wants.
                if (reader.ReadTextString() == "authData" && reader.PeekState() == CborReaderState.ByteString)
                {
                    found = reader.ReadByteString();
                }
                else
                {
                    reader.SkipValue();
                }
            }

            reader.ReadEndMap();

            if (found is null)
            {
                failure = "the attestation object carried no authenticator data";
                return false;
            }

            authData = found;
            failure = "";
            return true;
        }
        catch (Exception exception) when (exception is CborContentException or InvalidOperationException or OverflowException)
        {
            failure = "the attestation object was not valid CBOR";
            return false;
        }
    }

    private static bool TryReadAttestedCredential(
        byte[] authData,
        AuthenticatorFlags flags,
        out byte[] credentialId,
        out byte[] publicKey,
        out int algorithm,
        out string failure)
    {
        credentialId = [];
        publicKey = [];
        algorithm = 0;

        var offset = AuthenticatorDataHeaderLength;

        if (authData.Length < offset + AaguidLength + 2)
        {
            failure = "the attested credential data was too short";
            return false;
        }

        offset += AaguidLength;
        var length = BinaryPrimitives.ReadUInt16BigEndian(authData.AsSpan(offset, 2));
        offset += 2;

        if (length is 0 or > MaxCredentialIdLength || authData.Length < offset + length)
        {
            failure = "the credential id length was out of range";
            return false;
        }

        var id = authData.AsSpan(offset, length).ToArray();
        offset += length;

        using var key = CoseKey.Read(authData.AsMemory(offset), out var read);
        if (key is null)
        {
            failure = "the credential public key was missing or unsupported";
            return false;
        }

        credentialId = id;
        publicKey = authData.AsSpan(offset, read).ToArray();
        algorithm = key.Algorithm;
        offset += read;

        // Extension data is allowed to follow the key, and only then: trailing bytes with no ED flag mean this is not
        // the structure it claims to be.
        if ((flags & AuthenticatorFlags.ExtensionData) == 0 && offset != authData.Length)
        {
            failure = "the authenticator data had trailing bytes";
            return false;
        }

        failure = "";
        return true;
    }

    private static bool TryDecode(string? value, out byte[] bytes)
    {
        bytes = [];

        if (string.IsNullOrEmpty(value) || value.Length > MaxFieldLength)
        {
            return false;
        }

        try
        {
            bytes = WebEncoders.Base64UrlDecode(value);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

/// <summary>The flag byte of authenticator data (WebAuthn L2 §6.1).</summary>
[Flags]
internal enum AuthenticatorFlags : byte
{
    /// <summary>Nothing set.</summary>
    None = 0,

    /// <summary>UP — somebody was there and touched it.</summary>
    UserPresent = 1 << 0,

    /// <summary>UV — and proved who they were, with a biometric or a PIN.</summary>
    UserVerified = 1 << 2,

    /// <summary>BE — the credential may be backed up.</summary>
    BackupEligible = 1 << 3,

    /// <summary>BS — the credential is backed up, which is what makes a passkey synced across a user's devices.</summary>
    BackedUp = 1 << 4,

    /// <summary>AT — attested credential data follows, which a registration carries and an assertion does not.</summary>
    AttestedCredentialData = 1 << 6,

    /// <summary>ED — extension data follows.</summary>
    ExtensionData = 1 << 7,
}
