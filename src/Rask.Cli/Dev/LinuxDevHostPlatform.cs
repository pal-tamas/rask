using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Rask.Cli.Dev;

/// <summary>
///     Linux: the distribution's CA anchor directory, NSS for the browsers that ignore it, and one
///     sysctl so Kestrel may bind 443.
/// </summary>
/// <remarks>
///     <para>
///         The awkward platform, because "trust a certificate" is not one operation. The system anchor
///         store covers curl, wget and .NET itself, and its directory and refresh command differ per
///         distribution. Chrome and Firefox consult neither: they use NSS databases, one per user for
///         Chrome and one per profile for Firefox, reachable only through <c>certutil</c> — which is a
///         separate package (<c>libnss3-tools</c>) that is frequently absent. Each store is handled
///         independently and best-effort, so a missing <c>certutil</c> costs Chrome and Firefox rather
///         than failing the whole setup.
///     </para>
///     <para>
///         Port 443 is a single sysctl rather than a firewall rule. That is the deliberate choice:
///         Linux firewall tables are actively managed by docker, ufw and firewalld, and injecting a
///         redirect into them is far more likely to collide with something the developer depends on
///         than it is on macOS. <c>ip_unprivileged_port_start</c> touches no rules at all, is one value
///         to put back, and resets on reboot. The cost is that it is machine-wide — any unprivileged
///         process may then bind 443 and up — which is a real widening, and the reason it is named
///         explicitly in the confirmation prompt.
///     </para>
/// </remarks>
internal sealed class LinuxDevHostPlatform(IProcessRunner process, IConsole console)
    : DevHostPlatform(process, console)
{
    /// <summary>The sysctl that decides the lowest port an unprivileged process may bind.</summary>
    private const string PortSysctl = "net.ipv4.ip_unprivileged_port_start";

    /// <summary>Where Chrome and anything else using the shared per-user NSS database looks.</summary>
    private static string ChromeNssDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".pki", "nssdb");

    /// <summary>
    ///     The anchor directory and refresh command for this distribution, or null when it is one we do
    ///     not recognise.
    /// </summary>
    private static (string Directory, string Command, string[] Arguments)? Anchors =>
        Directory.Exists("/usr/local/share/ca-certificates")
            ? ("/usr/local/share/ca-certificates", "update-ca-certificates", [])
            : Directory.Exists("/etc/pki/ca-trust/source/anchors")
                ? ("/etc/pki/ca-trust/source/anchors", "update-ca-trust", ["extract"])
                : Directory.Exists("/etc/ca-certificates/trust-source/anchors")
                    ? ("/etc/ca-certificates/trust-source/anchors", "trust", ["extract-compat"])
                    : null;

    private static string? AnchorPath => Anchors is { } anchors
        ? Path.Combine(anchors.Directory, "rask-local-ca.crt")
        : null;

    public override string HostsPath => "/etc/hosts";

    /// <summary>443 directly, once the sysctl below allows it.</summary>
    public override int HttpsPort => 443;

    public override string TrustChange =>
        $"trust '{DevCertificates.AuthorityName}' as a local certificate authority (system CA anchors)";

    public override string? PortChange =>
        $"allow this user to bind port 443 (sysctl {PortSysctl}, until reboot)";

    public override async Task<bool> IsTrustedAsync(string thumbprint, CancellationToken cancellationToken)
    {
        if (AnchorPath is not { } anchor || !File.Exists(anchor))
        {
            return false;
        }

        try
        {
            using var installed = X509CertificateLoader.LoadCertificateFromFile(anchor);
            if (!string.Equals(installed.Thumbprint, thumbprint, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }
        catch (CryptographicException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }

        // The system anchor is in place. If certutil is unavailable there is nothing further that could
        // be done anyway, so reporting "not trusted" would only re-run a step that cannot progress.
        if (!await HasCertutilAsync(cancellationToken).ConfigureAwait(false) || !Directory.Exists(ChromeNssDirectory))
        {
            return true;
        }

        var found = await CaptureAsync(
            "certutil",
            ["-d", "sql:" + ChromeNssDirectory, "-L", "-n", DevCertificates.AuthorityName],
            cancellationToken).ConfigureAwait(false);

        return found.ExitCode == 0;
    }

    public override async Task<bool> TrustAsync(string certificatePath, CancellationToken cancellationToken)
    {
        if (Anchors is not { } anchors || AnchorPath is not { } anchor)
        {
            Console.WriteErrorLine(
                "Couldn't find this distribution's CA anchor directory, so the certificate can't be trusted system-wide.",
                ConsoleStyle.Warning);
            return false;
        }

        // The anchor must be a PEM with a .crt extension on every one of these distributions.
        if (await RunAsync(
                "sudo",
                ["-n", "install", "-m", "0644", "-o", "root", "-g", "root", certificatePath, anchor],
                cancellationToken).ConfigureAwait(false) != 0)
        {
            return false;
        }

        if (await RunAsync(
                "sudo",
                ["-n", anchors.Command, .. anchors.Arguments],
                cancellationToken).ConfigureAwait(false) != 0)
        {
            return false;
        }

        // Best-effort from here: the system store is what makes the setup correct, and NSS is what makes
        // the browsers agree. Failing the whole thing because Firefox is fussy would be the wrong trade.
        await TrustInNssAsync(certificatePath, cancellationToken).ConfigureAwait(false);
        return true;
    }

    public override async Task<bool> InstallHostsAsync(string stagedPath, CancellationToken cancellationToken) =>
        await RunAsync(
            "sudo",
            ["-n", "install", "-m", "0644", "-o", "root", "-g", "root", stagedPath, HostsPath],
            cancellationToken).ConfigureAwait(false) == 0;

    /// <summary>
    ///     The sysctl value to set, or null when this machine already lets us bind 443.
    /// </summary>
    /// <remarks>
    ///     Read live rather than remembered. Unlike a pf anchor this value is readable without any
    ///     privilege, so there is nothing to cache and nothing that can go stale — including across the
    ///     reboot that resets it.
    /// </remarks>
    public override async Task<string?> RequiredPortSetupAsync(CancellationToken cancellationToken)
    {
        var current = await CaptureAsync("sysctl", ["-n", PortSysctl], cancellationToken).ConfigureAwait(false);

        if (current.ExitCode == 0
            && int.TryParse(current.StandardOutput.Trim(), CultureInfo.InvariantCulture, out var start)
            && start <= HttpsPort)
        {
            return null;
        }

        return HttpsPort.ToString(CultureInfo.InvariantCulture);
    }

    public override async Task<bool> ApplyPortSetupAsync(string token, CancellationToken cancellationToken) =>
        await RunAsync(
            "sudo",
            ["-n", "sysctl", "-w", $"{PortSysctl}={token}"],
            cancellationToken).ConfigureAwait(false) == 0;

    public override async Task<bool> PrimePrivilegeAsync(CancellationToken cancellationToken) =>
        await RunAsync("sudo", ["-v"], cancellationToken).ConfigureAwait(false) == 0;

    public override async Task WriteFollowUpAsync(CancellationToken cancellationToken)
    {
        // Nothing to say when certutil did the work — unlike the other platforms, Linux can reach
        // Chrome's and Firefox's stores, so the usual Firefox caveat does not apply here.
        if (!await HasCertutilAsync(cancellationToken).ConfigureAwait(false))
        {
            Console.WriteLine(
                "  Chrome and Firefox keep their own certificate stores. Install 'certutil' "
                + "(libnss3-tools) and run `rask dev` again to have them trust this too.",
                ConsoleStyle.Dim);
        }
    }

    /// <summary>
    ///     Adds the authority to the per-user NSS database Chrome reads, and to every Firefox profile.
    /// </summary>
    private async Task TrustInNssAsync(string certificatePath, CancellationToken cancellationToken)
    {
        if (!await HasCertutilAsync(cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        foreach (var database in NssDatabases())
        {
            try
            {
                Directory.CreateDirectory(database);
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            // -t C,, marks it trusted for TLS server authentication and nothing else. Deleted first so a
            // re-issued authority replaces the old entry instead of colliding with its nickname.
            await RunAsync(
                "certutil",
                ["-d", "sql:" + database, "-D", "-n", DevCertificates.AuthorityName],
                cancellationToken).ConfigureAwait(false);

            await RunAsync(
                "certutil",
                ["-d", "sql:" + database, "-A", "-t", "C,,", "-n", DevCertificates.AuthorityName, "-i", certificatePath],
                cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Chrome's shared database, plus one per Firefox profile.</summary>
    private static IEnumerable<string> NssDatabases()
    {
        yield return ChromeNssDirectory;

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        foreach (var root in new[]
                 {
                     Path.Combine(home, ".mozilla", "firefox"),
                     Path.Combine(home, "snap", "firefox", "common", ".mozilla", "firefox"),
                 })
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            string[] profiles;
            try
            {
                // A profile is a directory that already holds an NSS database; anything else there is
                // not one, and creating a database inside it would leave Firefox ignoring it anyway.
                profiles = Directory.GetDirectories(root)
                    .Where(directory => File.Exists(Path.Combine(directory, "cert9.db")))
                    .ToArray();
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var profile in profiles)
            {
                yield return profile;
            }
        }
    }

    /// <summary>
    ///     Whether <c>certutil</c> (from <c>libnss3-tools</c>) is installed.
    /// </summary>
    /// <remarks>
    ///     Asked by running it rather than by looking for it on PATH: <c>command -v</c> is a shell
    ///     builtin and not something that can be spawned. Its exit code is not the signal — <c>-H</c>
    ///     prints usage and exits non-zero on some builds — so what is being tested is whether the
    ///     process starts at all.
    /// </remarks>
    private async Task<bool> HasCertutilAsync(CancellationToken cancellationToken)
    {
        try
        {
            await CaptureAsync("certutil", ["-H"], cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }
}
