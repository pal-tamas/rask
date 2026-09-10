using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Rask.Cli.Dev;

namespace Rask.Cli.E2E.Tests;

/// <summary>
///     The Linux dev host, carried out for real against the machine the test is running on.
/// </summary>
/// <remarks>
///     <para>
///         Every other <see cref="DevHostTests" /> assertion is a pure function or a fake process runner,
///         which proves the argv Rask <em>builds</em> and nothing about whether the machine ends up
///         trusting anything. This closes that gap: the CA anchor is really installed, the distribution's
///         refresh command really runs, <c>certutil</c> really writes NSS, <c>/etc/hosts</c> is really
///         rewritten, and the sysctl is really set — then <c>curl</c> completes a TLS handshake to
///         <c>https://appname.test</c> validating against the <b>system trust store</b>, with no
///         <c>--cacert</c> and nothing told about the authority.
///     </para>
///     <para>
///         It is destructive by nature — it modifies the trust store, the hosts file and a sysctl — so it
///         is opt-in and belongs in a throwaway container. <c>scripts/run-devhost-linux-local.sh</c>
///         builds that container and runs exactly this class inside it.
///     </para>
/// </remarks>
public sealed class DevHostLinuxE2ETests
{
    private const string Hostname = "appname.test";

    internal const string SkipReason =
        "Linux dev-host gate: set RASK_DEVHOST_E2E=1 to run it. It rewrites /etc/hosts, the system CA "
        + "anchors and a sysctl, so run it in a container — see scripts/run-devhost-linux-local.sh.";

    private static bool Enabled => Environment.GetEnvironmentVariable("RASK_DEVHOST_E2E") == "1";

    [SkippableFact]
    public async Task The_whole_linux_path_ends_in_a_trusted_handshake()
    {
        Skip.IfNot(Enabled, SkipReason);
        Skip.IfNot(OperatingSystem.IsLinux(), "The Linux dev host only applies to Linux.");

        var cancellationToken = CancellationToken.None;
        var console = new StringConsole();
        var store = new DevHostStore(Path.Combine(Path.GetTempPath(), "rask-devhost-" + Guid.NewGuid().ToString("N")));
        var platform = new LinuxDevHostPlatform(new ProcessRunner(), console);

        // ---- mint and issue, exactly as `rask dev` does ----
        var authority = DevCertificates.CreateAuthority(DateTimeOffset.Now);
        store.WriteAuthority(authority);
        store.WriteCertificate(
            Hostname,
            DevCertificates.IssueServerCertificate(authority, Hostname, DateTimeOffset.Now));

        using var certificateAuthority = DevCertificates.Load(authority);

        // Nothing has been installed yet, so the machine must say so. If this were already true the rest
        // of the test would prove nothing.
        Assert.False(await platform.IsTrustedAsync(certificateAuthority.Thumbprint, cancellationToken));

        // ---- trust: system anchors, then NSS for Chrome and Firefox ----
        Assert.True(
            await platform.TrustAsync(store.AuthorityCertificatePath, cancellationToken),
            "TrustAsync failed: " + console.Error);

        Assert.True(
            await platform.IsTrustedAsync(certificateAuthority.Thumbprint, cancellationToken),
            "the authority was installed but is not reported as trusted");

        // ---- the hosts file ----
        var hosts = DevHostFiles.AddHost(await File.ReadAllTextAsync(platform.HostsPath, cancellationToken), Hostname);
        Assert.NotNull(hosts);

        var staged = Path.Combine(Path.GetTempPath(), "rask-hosts-" + Guid.NewGuid().ToString("N"));
        await File.WriteAllTextAsync(staged, hosts!, cancellationToken);

        Assert.True(await platform.InstallHostsAsync(staged, cancellationToken), "InstallHostsAsync failed");

        // Resolved by the resolver, not merely present in the file — which is the thing that actually has
        // to be true for a browser to reach the app.
        var resolved = await Dns.GetHostAddressesAsync(Hostname, cancellationToken);
        Assert.Contains(IPAddress.Loopback, resolved);

        // ---- the port ----
        if (await platform.RequiredPortSetupAsync(cancellationToken) is { } token)
        {
            Assert.True(await platform.ApplyPortSetupAsync(token, cancellationToken), "ApplyPortSetupAsync failed");
        }

        // Idempotent: having applied it, the plan must come back empty — this is what stops `rask dev`
        // asking for a password on every run.
        Assert.Null(await platform.RequiredPortSetupAsync(cancellationToken));

        // ---- the whole point: a handshake nothing was told how to trust ----
        using var served = X509Certificate2.CreateFromPemFile(store.CertificatePath(Hostname), store.KeyPath(Hostname));
        using var listener = new TcpListener(IPAddress.Loopback, platform.HttpsPort);

        // Binding 443 as an unprivileged user is itself the assertion that the sysctl took effect.
        listener.Start();

        var server = ServeOnceAsync(listener, served, cancellationToken);

        // curl, not HttpClient: .NET caches the OpenSSL trust bundle for the life of the process, and this
        // process was running before the anchor was installed. A fresh process reads it fresh — and curl
        // is also what a developer would reach for to check this by hand.
        var (exit, output) = await CurlAsync(cancellationToken);

        await server;

        Assert.True(exit == 0, $"curl https://{Hostname} failed ({exit}): {output}");
        Assert.Contains("rask-dev-host-ok", output, StringComparison.Ordinal);
    }

    /// <summary>Accepts one TLS connection and answers it with a minimal HTTP response.</summary>
    private static async Task ServeOnceAsync(
        TcpListener listener,
        X509Certificate2 certificate,
        CancellationToken cancellationToken)
    {
        try
        {
            using var connection = await listener.AcceptTcpClientAsync(cancellationToken);
            await using var tls = new SslStream(connection.GetStream(), leaveInnerStreamOpen: false);
            await tls.AuthenticateAsServerAsync(certificate, false, checkCertificateRevocation: false);

            const string Body = "rask-dev-host-ok";
            var response = Encoding.ASCII.GetBytes(
                $"HTTP/1.1 200 OK\r\nContent-Type: text/plain\r\nContent-Length: {Body.Length}\r\nConnection: close\r\n\r\n{Body}");

            await tls.WriteAsync(response, cancellationToken);
            await tls.FlushAsync(cancellationToken);
        }
        finally
        {
            listener.Stop();
        }
    }

    /// <summary>
    ///     Fetches the page with no certificate hints of any kind — the system store is the only thing
    ///     that can make this succeed.
    /// </summary>
    private static async Task<(int Exit, string Output)> CurlAsync(CancellationToken cancellationToken)
    {
        var result = await new ProcessRunner().CaptureAsync(
            "curl",
            ["--silent", "--show-error", "--max-time", "20", $"https://{Hostname}/"],
            workingDirectory: null,
            cancellationToken);

        return (result.ExitCode, result.StandardOutput + result.StandardError);
    }
}
