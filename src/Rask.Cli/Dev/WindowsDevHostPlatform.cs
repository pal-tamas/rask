using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Rask.Hosting.Shared;

namespace Rask.Cli.Dev;

/// <summary>
///     Windows: the per-user root store, and an elevated copy over the hosts file. No port work at all.
/// </summary>
/// <remarks>
///     <para>
///         The least invasive of the three, for two reasons that are easy to get backwards. Windows has
///         no privileged-port range, so Kestrel binds 443 directly and there is nothing to redirect,
///         nothing to reload after a reboot, and no packet filter to interact with. And the
///         <c>CurrentUser\Root</c> store is writable without elevation — Windows shows its own consent
///         dialog instead — so the authority is trusted through a plain .NET API rather than by shelling
///         out at all. Chrome and Edge both read that store.
///     </para>
///     <para>
///         That leaves the hosts file as the only step that needs elevation, which is what the UAC
///         prompt is for.
///     </para>
/// </remarks>
internal sealed class WindowsDevHostPlatform(IProcessRunner process, IConsole console)
    : DevHostPlatform(process, console)
{
    public override string HostsPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "drivers", "etc", "hosts");

    /// <summary>443 directly: Windows lets an ordinary process bind it.</summary>
    public override int HttpsPort => DevHostPaths.DirectHttpsPort;

    public override string TrustChange =>
        $"trust '{DevCertificates.AuthorityName}' as a local certificate authority (your user's Trusted Roots)";

    /// <summary>Nothing. Kestrel already answers on 443.</summary>
    public override string? PortChange => null;

    public override Task<bool> IsTrustedAsync(string thumbprint, CancellationToken cancellationToken)
    {
        try
        {
            using var store = new X509Store(StoreName.Root, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadOnly);

            // By fingerprint, so a stale authority from an earlier install — same subject, different key
            // — is replaced rather than mistaken for a match.
            return Task.FromResult(store.Certificates
                .Any(certificate => string.Equals(certificate.Thumbprint, thumbprint, StringComparison.OrdinalIgnoreCase)));
        }
        catch (CryptographicException)
        {
            return Task.FromResult(false);
        }
    }

    public override Task<bool> TrustAsync(string certificatePath, CancellationToken cancellationToken)
    {
        try
        {
            using var certificate = X509CertificateLoader.LoadCertificateFromFile(certificatePath);
            using var store = new X509Store(StoreName.Root, StoreLocation.CurrentUser);

            // Windows raises its own "you are about to install a root certificate" dialog here. Declining
            // it surfaces as a CryptographicException, which is a refusal rather than a fault — so it
            // falls back to localhost like any other declined step.
            store.Open(OpenFlags.ReadWrite);
            store.Add(certificate);

            return Task.FromResult(true);
        }
        catch (CryptographicException)
        {
            return Task.FromResult(false);
        }
        catch (IOException)
        {
            return Task.FromResult(false);
        }
    }

    public override async Task<bool> InstallHostsAsync(string stagedPath, CancellationToken cancellationToken)
    {
        // Both paths are interpolated into a PowerShell single-quoted literal below. One is a constant
        // and the other is a GUID under the temp directory, so neither can contain a quote — but this is
        // the boundary where that would stop being true, so it is checked rather than assumed.
        if (!IsQuoteFree(stagedPath) || !IsQuoteFree(HostsPath))
        {
            return false;
        }

        // Start-Process -Verb RunAs is the UAC prompt. -Wait so the copy has finished before the caller
        // reads the file back, and the inner copy is a single Copy-Item rather than a script, so there
        // is nothing for an unexpected path to expand into.
        var script =
            $"Start-Process -FilePath cmd.exe -Verb RunAs -Wait -WindowStyle Hidden "
            + $"-ArgumentList '/c','copy','/y','\"{stagedPath}\"','\"{HostsPath}\"'";

        var exit = await RunAsync(
            "powershell",
            ["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", script],
            cancellationToken).ConfigureAwait(false);

        return exit == 0;
    }

    /// <summary>Never anything to do: 443 needs no help here.</summary>
    public override Task<string?> RequiredPortSetupAsync(CancellationToken cancellationToken) =>
        Task.FromResult<string?>(null);

    public override Task<bool> ApplyPortSetupAsync(string token, CancellationToken cancellationToken) =>
        Task.FromResult(true);

    /// <summary>
    ///     Nothing to prime. Elevation on Windows is a UAC dialog raised by the one step that needs it,
    ///     not a credential cache that can be warmed in advance.
    /// </summary>
    public override Task<bool> PrimePrivilegeAsync(CancellationToken cancellationToken) => Task.FromResult(true);

    private static bool IsQuoteFree(string path) =>
        !path.Contains('\'', StringComparison.Ordinal) && !path.Contains('"', StringComparison.Ordinal);
}
