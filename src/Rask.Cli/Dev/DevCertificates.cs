using System.Formats.Asn1;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Rask.Hosting.Shared;

namespace Rask.Cli.Dev;

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
    private const string NameConstraintsOid = "2.5.29.30";

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

    /// <summary>
    ///     The loopback names every issued server certificate also covers, beside its own
    ///     <c>.test</c> name. Kestrel binds loopback, so these reach the very same app.
    /// </summary>
    public static readonly IReadOnlyList<string> LoopbackNames = ["localhost"];

    /// <summary>
    ///     The loopback addresses every issued server certificate also covers. A bare IP in a URL is
    ///     matched against <c>iPAddress</c> entries, never against a DNS name, so <c>127.0.0.1</c> has
    ///     to be listed as an address to be accepted.
    /// </summary>
    public static readonly IReadOnlyList<IPAddress> LoopbackAddresses = [IPAddress.Loopback, IPAddress.IPv6Loopback];

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
        // itself an authority, so a leaked key cannot mint a subordinate CA.
        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(certificateAuthority: true, hasPathLengthConstraint: true, pathLengthConstraint: 0, critical: true));

        // And only for the names `rask dev` serves. This root sits in the system trust store, so
        // without it the key under ~/.rask could sign a certificate every browser on the machine
        // accepts for ANY site — a bank, a mail provider. Critical, so a verifier that does not
        // understand the constraint rejects the chain rather than ignoring it.
        request.CertificateExtensions.Add(NameConstraints());

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

        request.CertificateExtensions.Add(SubjectAlternativeNames(hostname));

        request.CertificateExtensions.Add(
            new X509SubjectKeyIdentifierExtension(request.PublicKey, critical: false));

        request.CertificateExtensions.Add(
            X509AuthorityKeyIdentifierExtension.CreateFromCertificate(
                authorityCertificate, includeKeyIdentifier: true, includeIssuerAndSerial: false));

        var notBefore = now.AddHours(-1);
        var notAfter = now + ServerLifetime;

        // Never outlive the authority that signed it: a chain is only as valid as its root, and a leaf
        // that claims more would fail verification in a way that points at the wrong certificate.
        if (notAfter > new DateTimeOffset(authorityCertificate.NotAfter))
        {
            notAfter = new DateTimeOffset(authorityCertificate.NotAfter);
        }

        using var issued = request.Create(authorityCertificate, notBefore, notAfter, SerialNumber());

        return Export(issued, key);
    }

    /// <summary>
    ///     The names the authority may sign for: anything under <c>.test</c>, and loopback by name and
    ///     by address — exactly what <see cref="SubjectAlternativeNames" /> puts in a leaf.
    /// </summary>
    /// <remarks>
    ///     .NET has a reader for most extensions and a builder for few, and none for this one, so it is
    ///     written out (RFC 5280 §4.2.1.10): a sequence holding <c>permittedSubtrees [0]</c>, each entry
    ///     a <c>dNSName [2]</c> or an <c>iPAddress [7]</c>. A DNS constraint covers the name and every
    ///     name beneath it; an address constraint is the address followed by its mask.
    /// </remarks>
    private static X509Extension NameConstraints()
    {
        var writer = new AsnWriter(AsnEncodingRules.DER);

        using (writer.PushSequence())
        using (writer.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 0)))
        {
            foreach (var name in (string[])[DevHostName.Suffix, .. LoopbackNames])
            {
                using (writer.PushSequence())
                {
                    writer.WriteCharacterString(
                        UniversalTagNumber.IA5String, name, new Asn1Tag(TagClass.ContextSpecific, 2));
                }
            }

            foreach (var address in LoopbackAddresses)
            {
                var bytes = address.GetAddressBytes();
                var mask = new byte[bytes.Length];
                mask.AsSpan().Fill(0xFF);

                using (writer.PushSequence())
                {
                    writer.WriteOctetString([.. bytes, .. mask], new Asn1Tag(TagClass.ContextSpecific, 7));
                }
            }
        }

        return new X509Extension(NameConstraintsOid, writer.Encode(), critical: true);
    }

    private static bool IsConstrained(X509Certificate2 authority) =>
        authority.Extensions.Any(
            extension => string.Equals(extension.Oid?.Value, NameConstraintsOid, StringComparison.Ordinal));

    private static X509Extension SubjectAlternativeNames(string hostname)
    {
        // The subject alternative name is what browsers actually read; a CN alone has not been
        // accepted by Chrome or Safari for years. Both the bare host and a wildcard beneath it, so a
        // future `api.appname.test` needs no second certificate.
        var alternativeNames = new SubjectAlternativeNameBuilder();
        alternativeNames.AddDnsName(hostname);
        alternativeNames.AddDnsName("*." + hostname);

        // And the loopback names, because this is the same listener. The name is what we open and what
        // the docs promise, but it is not the only thing that reaches Kestrel: an older scaffold's
        // launchSettings.json, a bookmark, an IDE's run button or `--no-host` all arrive on
        // https://localhost:PORT instead. Without these a certificate covering only the `.test` name
        // turns every one of those into a name-mismatch interstitial — the browser refusing to load a
        // page that IS the app, which reads as "Rask is broken" rather than "use the other URL".
        foreach (var loopback in LoopbackNames)
        {
            alternativeNames.AddDnsName(loopback);
        }

        foreach (var address in LoopbackAddresses)
        {
            alternativeNames.AddIpAddress(address);
        }

        return alternativeNames.Build();
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
            // An authority minted before it was confined to these names could sign for any site, and it
            // is in the trust store: replaced on sight, rather than left there until it nears expiry.
            return loaded.NotAfter - now.LocalDateTime <= RenewalWindow
                   || (hostname is null ? !IsConstrained(loaded) : !CoversEveryName(loaded, hostname));
        }
        catch (CryptographicException)
        {
            // Truncated, corrupted, or written by a version that shaped it differently. Whatever it is,
            // it is not something to serve TLS with, and re-minting costs milliseconds.
            return true;
        }
    }

    /// <summary>
    ///     True when <paramref name="certificate" /> covers its own name AND every loopback name we now
    ///     add. The loopback check is what re-mints the certificates issued before they were added:
    ///     those still match their <c>.test</c> name perfectly, so a hostname-only test would call them
    ///     good forever and leave the machine on a certificate that rejects
    ///     <c>https://localhost:PORT</c> — the exact URL an older scaffold opens.
    /// </summary>
    private static bool CoversEveryName(X509Certificate2 certificate, string hostname) =>
        certificate.MatchesHostname(hostname)
        && LoopbackNames.All(name => certificate.MatchesHostname(name))
        && LoopbackAddresses.All(address => certificate.MatchesHostname(address.ToString()));

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
