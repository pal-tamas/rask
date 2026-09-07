using System.Globalization;
using System.Text.Json;

namespace Rask.Cli.Dev;

/// <summary>
///     Everything <c>rask dev</c> keeps between runs so the second run costs no password: the local
///     authority, the certificate issued for each hostname, and a note of what was last loaded into pf.
/// </summary>
/// <remarks>
///     Rooted at a directory the caller supplies (<c>~/.rask</c> in production) so tests drive the real
///     code against a temporary directory instead of the developer's own machine.
/// </remarks>
internal sealed class DevHostStore(string root)
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    /// <summary>Where certificates and keys live.</summary>
    private string CertificateDirectory => Path.Combine(root, "certs");

    /// <summary>The record of what this tool last asked the kernel for. See <see cref="ReadPfState" />.</summary>
    private string PfStatePath => Path.Combine(root, "pf-state.json");

    public string AuthorityCertificatePath => Path.Combine(CertificateDirectory, "rask-local-ca.pem");

    private string AuthorityKeyPath => Path.Combine(CertificateDirectory, "rask-local-ca.key");

    public string CertificatePath(string hostname) => Path.Combine(CertificateDirectory, hostname + ".pem");

    public string KeyPath(string hostname) => Path.Combine(CertificateDirectory, hostname + ".key");

    /// <summary>The stored authority, or null when this machine has never minted one.</summary>
    public DevCertificate? ReadAuthority() => Read(AuthorityCertificatePath, AuthorityKeyPath);

    public void WriteAuthority(DevCertificate authority) =>
        Write(AuthorityCertificatePath, AuthorityKeyPath, authority);

    /// <summary>The stored certificate for <paramref name="hostname" />, or null when there is none.</summary>
    public DevCertificate? ReadCertificate(string hostname) =>
        Read(CertificatePath(hostname), KeyPath(hostname));

    public void WriteCertificate(string hostname, DevCertificate certificate) =>
        Write(CertificatePath(hostname), KeyPath(hostname), certificate);

    private DevCertificate? Read(string certificatePath, string keyPath)
    {
        try
        {
            if (!File.Exists(certificatePath) || !File.Exists(keyPath))
            {
                return null;
            }

            return new DevCertificate(File.ReadAllText(certificatePath), File.ReadAllText(keyPath));
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private void Write(string certificatePath, string keyPath, DevCertificate certificate)
    {
        Directory.CreateDirectory(CertificateDirectory);
        RestrictToOwner(CertificateDirectory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        File.WriteAllText(certificatePath, certificate.CertificatePem);
        File.WriteAllText(keyPath, certificate.PrivateKeyPem);

        // The private key is the whole security boundary of this feature: anyone who can read it can
        // mint a certificate the developer's browser trusts. Written first, then narrowed, because
        // File.WriteAllText creates with the process umask.
        RestrictToOwner(keyPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    private static void RestrictToOwner(string path, UnixFileMode mode)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            File.SetUnixFileMode(path, mode);
        }
        catch (IOException)
        {
            // A filesystem that does not carry permissions (a mounted share, a container overlay).
            // Nothing better to do, and failing here would take down a dev loop over file metadata.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    ///     What this tool last loaded into pf, and the boot it loaded it on — or null when it has
    ///     loaded nothing.
    /// </summary>
    /// <remarks>
    ///     Reading pf's live state needs root, so checking the kernel directly would cost a password on
    ///     every single run and defeat the point. Instead the rules are recorded here alongside the
    ///     boot time they were loaded on: pf anchors do not survive a reboot, so a changed boot time
    ///     means the rules are gone even though the file still describes them. Cheap, and readable
    ///     without any privilege at all.
    /// </remarks>
    public (string Rules, string BootId)? ReadPfState()
    {
        try
        {
            if (!File.Exists(PfStatePath))
            {
                return null;
            }

            var state = JsonSerializer.Deserialize<PfState>(File.ReadAllText(PfStatePath));
            return state?.Rules is { } rules && state.BootId is { } bootId ? (rules, bootId) : null;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    public void WritePfState(string rules, string bootId)
    {
        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(PfStatePath, JsonSerializer.Serialize(new PfState(rules, bootId), Json));
        }
        catch (IOException)
        {
            // Losing the note costs one redundant reload next time, not correctness.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>The default root: <c>~/.rask</c>.</summary>
    public static string DefaultRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".rask");

    /// <summary>
    ///     A token that changes when the machine reboots, used to notice that pf has been reset.
    /// </summary>
    public static string BootId(string? rawBootTime) =>
        string.IsNullOrWhiteSpace(rawBootTime)
            // Unknown: fall back to a value that changes every day, so a machine we cannot read the
            // boot time on reloads at most once a day rather than on every run.
            ? DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : rawBootTime.Trim();

    private sealed record PfState(string? Rules, string? BootId);
}
