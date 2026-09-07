using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Rask.Cli.Dev;

/// <summary>A minted certificate and the private key that goes with it, both as PEM text.</summary>
internal readonly record struct DevCertificate(string CertificatePem, string PrivateKeyPem);

/// <summary>
///     Mints the local certificate authority <c>rask dev</c> trusts once, and the short-lived server
///     certificates it issues from that authority for each <c>.test</c> hostname.
/// </summary>
/// <remarks>
///     <para>
///         A CA rather than a pile of self-signed certificates, because trusting a certificate is the
///         one step that needs the developer's password and their attention. With an authority that
///         happens exactly once on this machine; every project afterwards gets a certificate that is
///         already trusted, with no prompt and nothing to confirm. Self-signed leaves would move that
///         prompt to every new project, which is precisely the friction this feature exists to remove.
///     </para>
///     <para>
///         Everything here is .NET's own X.509 stack. That is deliberate — the whole point of the
///         feature is that a working <c>https://appname.test</c> costs no installs, so reaching for
///         <c>openssl</c> or <c>mkcert</c> to mint the certificate would defeat it.
///     </para>
///     <para>
///         <c>dotnet dev-certs https</c> cannot stand in for any of this. It has no hostname or SAN
///         option of any kind — it only ever mints <c>CN=localhost</c> — so a certificate valid for
///         <c>appname.test</c> has to be created separately.
///     </para>
/// </remarks>
internal static class DevCertificates
{
    /// <summary>The authority's subject, as it appears in Keychain Access.</summary>
    public const string AuthorityName = "Rask Local Development CA";

    /// <summary>OID for TLS server authentication.</summary>
    private const string ServerAuthenticationOid = "1.3.6.1.5.5.7.3.1";

    /// <summary>
    ///     How long a newly minted authority is good for. Long, because re-trusting it is the one step
    ///     that costs a password.
    /// </summary>
    private static readonly TimeSpan AuthorityLifetime = TimeSpan.FromDays(365 * 10);

    /// <summary>
    ///     How long an issued server certificate is good for.
    /// </summary>
    /// <remarks>
    ///     Apple caps publicly-trusted server certificates at 398 days and exempts certificates issued
    ///     by user-installed roots, so this could be longer. It is not, because staying inside the
    ///     stricter rule costs nothing and means a future tightening of that exemption cannot silently
    ///     break every developer's dev loop. Renewal is automatic and takes milliseconds.
    /// </remarks>
    private static readonly TimeSpan ServerLifetime = TimeSpan.FromDays(365);

    /// <summary>
    ///     Certificates are re-issued once they are inside this window of expiry, so one never expires
    ///     mid-session.
    /// </summary>
    public static readonly TimeSpan RenewalWindow = TimeSpan.FromDays(30);

    /// <summary>Mints a fresh local authority.</summary>
    public static DevCertificate CreateAuthority(DateTimeOffset now)
    {
        using var key = RSA.Create(2048);

        var request = new CertificateRequest(
            $"CN={AuthorityName}, O=Rask",
            key,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        // pathLengthConstraint 0: this authority may sign server certificates and nothing that is
        // itself an authority. If the key ever leaked it still could not be used to mint a subordinate
        // CA, which is the difference between "can impersonate .test sites" and "can impersonate
        // anything at all".
        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(certificateAuthority: true, hasPathLengthConstraint: true, pathLengthConstraint: 0, critical: true));

        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, critical: true));

        request.CertificateExtensions.Add(
            new X509SubjectKeyIdentifierExtension(request.PublicKey, critical: false));

        // Backdated an hour so a machine whose clock is slightly behind ours does not reject a
        // certificate that is legitimately valid.
        using var certificate = request.CreateSelfSigned(now.AddHours(-1), now + AuthorityLifetime);

        return Export(certificate, key);
    }

    /// <summary>
    ///     Issues a server certificate for <paramref name="hostname" />, signed by the authority.
    /// </summary>
    public static DevCertificate IssueServerCertificate(DevCertificate authority, string hostname, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hostname);

        using var authorityCertificate = Load(authority);
        using var key = RSA.Create(2048);

        var request = new CertificateRequest(
            $"CN={hostname}",
            key,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(certificateAuthority: false, hasPathLengthConstraint: false, pathLengthConstraint: 0, critical: true));

        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, critical: true));

        request.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension([new Oid(ServerAuthenticationOid)], critical: false));

        // The subject alternative name is what browsers actually read; a CN alone has not been
        // accepted by Chrome or Safari for years. Both the bare host and a wildcard beneath it, so a
        // future `api.appname.test` needs no second certificate.
        var alternativeNames = new SubjectAlternativeNameBuilder();
        alternativeNames.AddDnsName(hostname);
        alternativeNames.AddDnsName("*." + hostname);
        request.CertificateExtensions.Add(alternativeNames.Build());

        request.CertificateExtensions.Add(
            new X509SubjectKeyIdentifierExtension(request.PublicKey, critical: false));

        request.CertificateExtensions.Add(
            X509AuthorityKeyIdentifierExtension.CreateFromCertificate(
                authorityCertificate, includeKeyIdentifier: true, includeIssuerAndSerial: false));

        var notBefore = now.AddHours(-1);
        var notAfter = now + ServerLifetime;

        // Never outlive the authority that signed it: a chain is only as valid as its root, and a leaf
        // that claims more would fail verification in a way that points at the wrong certificate.
        if (notAfter > authorityCertificate.NotAfter)
        {
            notAfter = authorityCertificate.NotAfter;
        }

        using var issued = request.Create(authorityCertificate, notBefore, notAfter, SerialNumber());

        return Export(issued, key);
    }

    /// <summary>
    ///     True when <paramref name="certificate" /> is missing, unreadable, already covers a different
    ///     name, or is close enough to expiry to be worth replacing now rather than mid-session.
    /// </summary>
    /// <param name="hostname">
    ///     The name the certificate has to be valid for, or null to check only its dates. Null is what
    ///     the authority is checked with: it is not a server certificate, has no subject alternative
    ///     name, and asking whether it "matches a hostname" would answer no every time — re-minting the
    ///     authority on every run and asking for a password with it.
    /// </param>
    public static bool NeedsReissue(DevCertificate? certificate, string? hostname, DateTimeOffset now)
    {
        if (certificate is not { } present)
        {
            return true;
        }

        try
        {
            using var loaded = Load(present);
            return loaded.NotAfter - now.LocalDateTime <= RenewalWindow
                   || (hostname is not null && !loaded.MatchesHostname(hostname));
        }
        catch (CryptographicException)
        {
            // Truncated, corrupted, or written by a version that shaped it differently. Whatever it is,
            // it is not something to serve TLS with, and re-minting costs milliseconds.
            return true;
        }
    }

    /// <summary>Reads a PEM pair back into a usable certificate.</summary>
    public static X509Certificate2 Load(DevCertificate certificate) =>
        X509Certificate2.CreateFromPem(certificate.CertificatePem, certificate.PrivateKeyPem);

    private static DevCertificate Export(X509Certificate2 certificate, RSA key) =>
        new(
            new string(PemEncoding.Write("CERTIFICATE", certificate.RawData)),
            new string(PemEncoding.Write("PRIVATE KEY", key.ExportPkcs8PrivateKey())));

    /// <summary>
    ///     A random, positive 16-byte serial. The high bit is cleared because a serial is a signed
    ///     integer in DER, and a negative one is malformed.
    /// </summary>
    private static byte[] SerialNumber()
    {
        var serial = RandomNumberGenerator.GetBytes(16);
        serial[0] &= 0x7F;

        // A leading zero byte would also be re-encoded; nudging it to 1 keeps the value 16 bytes wide.
        if (serial[0] == 0)
        {
            serial[0] = 1;
        }

        return serial;
    }
}
