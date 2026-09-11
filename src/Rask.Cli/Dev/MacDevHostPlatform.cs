using Rask.Hosting.Shared;

namespace Rask.Cli.Dev;

/// <summary>
///     macOS: the system keychain, <c>/etc/hosts</c> through <c>install</c>, and a pf anchor for port
///     443 — the only one of the three platforms where an ordinary process cannot bind it.
/// </summary>
internal sealed class MacDevHostPlatform(IProcessRunner process, IConsole console, DevHostStore store)
    : DevHostPlatform(process, console)
{
    private const string SystemKeychain = "/Library/Keychains/System.keychain";

    public override string HostsPath => "/etc/hosts";

    /// <summary>
    ///     A high port, redirected. macOS reserves everything below 1024 for root, and running the whole
    ///     dev loop as root to get one port would leave every file it builds owned by root.
    /// </summary>
    public override int HttpsPort => DevHostPaths.RedirectedHttpsPort;

    public override string TrustChange =>
        $"trust '{DevCertificates.AuthorityName}' as a local certificate authority (System keychain)";

    public override string? PortChange => $"redirect port 443 to this app (pf anchor '{DevHostFiles.PfAnchor}')";

    public override async Task<bool> IsTrustedAsync(string thumbprint, CancellationToken cancellationToken)
    {
        // Reading the system keychain needs no privilege, which is what lets the common case — already
        // set up, nothing to do — cost nothing at all.
        var found = await CaptureAsync(
            "/usr/bin/security",
            ["find-certificate", "-c", DevCertificates.AuthorityName, "-Z", SystemKeychain],
            cancellationToken).ConfigureAwait(false);

        return found.ExitCode == 0
               && found.StandardOutput.Contains(thumbprint, StringComparison.OrdinalIgnoreCase);
    }

    public override async Task<bool> TrustAsync(string certificatePath, CancellationToken cancellationToken) =>
        // -d installs into the admin (system) domain so every browser and every tool on the machine
        // agrees; a login-keychain trust would leave Safari and curl disagreeing about the same name.
        await RunAsync(
            "sudo",
            ["-n", "/usr/bin/security", "add-trusted-cert", "-d", "-r", "trustRoot", "-k", SystemKeychain, certificatePath],
            cancellationToken).ConfigureAwait(false) == 0;

    /// <summary>
    ///     Overwrites the hosts file, writing through the existing file rather than replacing it.
    /// </summary>
    /// <remarks>
    ///     <c>cp</c> rather than <c>install</c>: <c>install</c> unlinks the destination and creates a new
    ///     one, which fails outright when <c>/etc/hosts</c> is a bind mount, a symlink or immutable.
    ///     Writing in place also leaves the file with the mode and ownership it already had, rather than
    ///     having a dev tool decide what those should be.
    /// </remarks>
    public override async Task<bool> InstallHostsAsync(string stagedPath, CancellationToken cancellationToken) =>
        await RunAsync("sudo", ["-n", "/bin/cp", stagedPath, HostsPath], cancellationToken).ConfigureAwait(false) == 0;

    /// <summary>
    ///     The pf rules this machine still needs, or null when they are already loaded.
    /// </summary>
    /// <remarks>
    ///     Reading pf's live state needs root, so asking the kernel directly would cost a password on
    ///     every run and defeat the point. The rules are recorded instead, alongside the boot they were
    ///     loaded on: a pf anchor does not survive a reboot, so a changed boot time means they are gone
    ///     even though the note still describes them.
    /// </remarks>
    public override async Task<string?> RequiredPortSetupAsync(CancellationToken cancellationToken)
    {
        if (DevHostFiles.PfRules(HttpsPort) is not { } rules)
        {
            return null;
        }

        if (store.ReadPfState() is not { } state || !string.Equals(state.Rules, rules, StringComparison.Ordinal))
        {
            return rules;
        }

        return string.Equals(state.BootId, await BootIdAsync(cancellationToken).ConfigureAwait(false), StringComparison.Ordinal)
            ? null
            : rules;
    }

    public override async Task<bool> ApplyPortSetupAsync(string token, CancellationToken cancellationToken)
    {
        var staged = Path.Combine(Path.GetTempPath(), "rask-pf-" + Guid.NewGuid().ToString("N"));

        try
        {
            await File.WriteAllTextAsync(staged, token, cancellationToken).ConfigureAwait(false);

            var loaded = await RunAsync(
                "sudo",
                ["-n", "/sbin/pfctl", "-a", DevHostFiles.PfAnchor, "-f", staged],
                cancellationToken).ConfigureAwait(false);

            if (loaded != 0)
            {
                return false;
            }

            // Rules in an anchor do nothing while pf itself is disabled, which is the macOS default.
            // Only enabled when it is actually off: `pfctl -E` bumps a reference count that is only
            // released by a matching -X, so enabling something already enabled slowly leaks tokens.
            if (!await IsPfEnabledAsync(cancellationToken).ConfigureAwait(false)
                && await RunAsync("sudo", ["-n", "/sbin/pfctl", "-E"], cancellationToken).ConfigureAwait(false) != 0)
            {
                return false;
            }

            store.WritePfState(token, await BootIdAsync(cancellationToken).ConfigureAwait(false));
            return true;
        }
        finally
        {
            try
            {
                File.Delete(staged);
            }
            catch (IOException)
            {
                // Tidying up a temp file is not worth failing over.
            }
        }
    }

    public override async Task<bool> PrimePrivilegeAsync(CancellationToken cancellationToken) =>
        await RunAsync("sudo", ["-v"], cancellationToken).ConfigureAwait(false) == 0;

    private async Task<bool> IsPfEnabledAsync(CancellationToken cancellationToken)
    {
        var info = await CaptureAsync("sudo", ["-n", "/sbin/pfctl", "-s", "info"], cancellationToken).ConfigureAwait(false);
        return info.ExitCode == 0 && info.StandardOutput.Contains("Status: Enabled", StringComparison.Ordinal);
    }

    private async Task<string> BootIdAsync(CancellationToken cancellationToken)
    {
        var boot = await CaptureAsync("/usr/sbin/sysctl", ["-n", "kern.boottime"], cancellationToken).ConfigureAwait(false);
        return DevHostStore.BootId(boot.ExitCode == 0 ? boot.StandardOutput : null);
    }
}
