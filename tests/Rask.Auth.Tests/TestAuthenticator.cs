using System.Buffers.Binary;
using System.Buffers.Text;
using System.Formats.Cbor;
using System.Security.Cryptography;
using System.Text;
using Rask.Wire;

namespace Rask.Auth.Tests;

/// <summary>
/// A software authenticator: it makes the bytes a real one makes, so the verifier can be tested without a browser.
/// </summary>
/// <remarks>
/// <para>
/// Everything here is built the way WebAuthn L2 §6.1 says an authenticator builds it — authenticator data, the
/// attested credential data, the COSE key, and a signature over <c>authData ∥ SHA-256(clientDataJSON)</c>. That is
/// what makes these tests worth something: they exercise the parser against structures assembled independently of it
/// rather than against whatever the parser happens to produce.
/// </para>
/// <para>
/// Every flag and counter is settable, so a test can be a broken or hostile authenticator as easily as a good one.
/// </para>
/// </remarks>
internal sealed class TestAuthenticator : IDisposable
{
    private readonly ECDsa? _ecdsa;
    private readonly RSA? _rsa;

    /// <param name="rsa">Whether to be an RS256 authenticator rather than the usual ES256 one.</param>
    /// <param name="credentialId">The credential id to answer with. A random 32-byte one by default.</param>
    public TestAuthenticator(bool rsa = false, byte[]? credentialId = null)
    {
        CredentialId = credentialId ?? RandomNumberGenerator.GetBytes(32);

        if (rsa)
        {
            _rsa = RSA.Create(2048);
        }
        else
        {
            _ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        }
    }

    /// <summary>The credential id this authenticator answers with.</summary>
    public byte[] CredentialId { get; }

    /// <summary>The counter it reports. Zero is what a synced passkey reports, forever.</summary>
    public uint SignCount { get; set; }

    /// <summary>Whether somebody touched it.</summary>
    public bool UserPresent { get; set; } = true;

    /// <summary>Whether they proved who they were.</summary>
    public bool UserVerified { get; set; } = true;

    /// <summary>Whether it says the credential is backed up — what makes a passkey synced.</summary>
    public bool BackedUp { get; set; }

    /// <summary>
    /// Whether to offer a key Rask does not accept — an Ed25519 (OKP) key, which is a real COSE key type and a real
    /// WebAuthn algorithm, just not one of the two this verifier reads.
    /// </summary>
    public bool Unsupported { get; init; }

    /// <summary>A registration, as <c>navigator.credentials.create</c> would return it.</summary>
    public PasskeyRegistrationRequest Register(
        string relyingPartyId, string origin, byte[] challenge, string state = "state", string? name = null)
    {
        var authData = AuthenticatorData(relyingPartyId, attested: true);

        var writer = new CborWriter(CborConformanceMode.Strict);
        writer.WriteStartMap(3);
        writer.WriteTextString("fmt");
        writer.WriteTextString("none");
        writer.WriteTextString("attStmt");
        writer.WriteStartMap(0);
        writer.WriteEndMap();
        writer.WriteTextString("authData");
        writer.WriteByteString(authData);
        writer.WriteEndMap();

        return new PasskeyRegistrationRequest(
            state,
            name,
            Base64Url.EncodeToString(CredentialId),
            Base64Url.EncodeToString(ClientData("webauthn.create", challenge, origin)),
            Base64Url.EncodeToString(writer.Encode()),
            ["internal"]);
    }

    /// <summary>An assertion, as <c>navigator.credentials.get</c> would return it.</summary>
    public PasskeyLoginRequest SignIn(
        string relyingPartyId,
        string origin,
        byte[] challenge,
        Guid? userHandle = null,
        string state = "state",
        bool remember = false)
    {
        var authData = AuthenticatorData(relyingPartyId, attested: false);
        var clientData = ClientData("webauthn.get", challenge, origin);

        var signed = new byte[authData.Length + 32];
        authData.CopyTo(signed, 0);
        SHA256.HashData(clientData, signed.AsSpan(authData.Length));

        return new PasskeyLoginRequest(
            state,
            Base64Url.EncodeToString(CredentialId),
            Base64Url.EncodeToString(clientData),
            Base64Url.EncodeToString(authData),
            Base64Url.EncodeToString(Sign(signed)),
            userHandle is { } id ? Base64Url.EncodeToString(id.ToByteArray()) : null,
            remember);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _ecdsa?.Dispose();
        _rsa?.Dispose();
    }

    private byte[] Sign(byte[] data) =>
        _ecdsa is not null
            ? _ecdsa.SignData(data, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence)
            : _rsa!.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

    private byte[] AuthenticatorData(string relyingPartyId, bool attested)
    {
        var flags = (byte)0;

        if (UserPresent)
        {
            flags |= 0x01;
        }

        if (UserVerified)
        {
            flags |= 0x04;
        }

        if (BackedUp)
        {
            // Backed up implies backup eligible, which is how a real authenticator reports a synced credential.
            flags |= 0x08 | 0x10;
        }

        if (attested)
        {
            flags |= 0x40;
        }

        var data = new List<byte>(SHA256.HashData(Encoding.UTF8.GetBytes(relyingPartyId))) { flags };

        var counter = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(counter, SignCount);
        data.AddRange(counter);

        if (!attested)
        {
            return [.. data];
        }

        // AAGUID: all zeroes is what an authenticator that declines to identify its model reports.
        data.AddRange(new byte[16]);

        var length = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(length, (ushort)CredentialId.Length);
        data.AddRange(length);
        data.AddRange(CredentialId);
        data.AddRange(CoseKey());

        return [.. data];
    }

    private byte[] CoseKey()
    {
        var writer = new CborWriter(CborConformanceMode.Strict);

        if (Unsupported)
        {
            // OKP / Ed25519: kty 1, alg -8, crv 6, x 32 bytes.
            writer.WriteStartMap(4);
            writer.WriteInt32(1);
            writer.WriteInt32(1);
            writer.WriteInt32(3);
            writer.WriteInt32(-8);
            writer.WriteInt32(-1);
            writer.WriteInt32(6);
            writer.WriteInt32(-2);
            writer.WriteByteString(RandomNumberGenerator.GetBytes(32));
            writer.WriteEndMap();

            return writer.Encode();
        }

        if (_ecdsa is not null)
        {
            var parameters = _ecdsa.ExportParameters(includePrivateParameters: false);

            writer.WriteStartMap(5);
            writer.WriteInt32(1);
            writer.WriteInt32(2);
            writer.WriteInt32(3);
            writer.WriteInt32(-7);
            writer.WriteInt32(-1);
            writer.WriteInt32(1);
            writer.WriteInt32(-2);
            writer.WriteByteString(parameters.Q.X!);
            writer.WriteInt32(-3);
            writer.WriteByteString(parameters.Q.Y!);
            writer.WriteEndMap();
        }
        else
        {
            var parameters = _rsa!.ExportParameters(includePrivateParameters: false);

            writer.WriteStartMap(4);
            writer.WriteInt32(1);
            writer.WriteInt32(3);
            writer.WriteInt32(3);
            writer.WriteInt32(-257);
            writer.WriteInt32(-1);
            writer.WriteByteString(parameters.Modulus!);
            writer.WriteInt32(-2);
            writer.WriteByteString(parameters.Exponent!);
            writer.WriteEndMap();
        }

        return writer.Encode();
    }

    private static byte[] ClientData(string type, byte[] challenge, string origin) =>
        Encoding.UTF8.GetBytes(
            $$"""{"type":"{{type}}","challenge":"{{Base64Url.EncodeToString(challenge)}}","origin":"{{origin}}","crossOrigin":false}""");
}
