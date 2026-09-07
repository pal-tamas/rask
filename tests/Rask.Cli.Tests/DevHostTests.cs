using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Rask.Cli.Dev;

namespace Rask.Cli.Tests;

/// <summary>
///     The <c>https://appname.test</c> dev host. Everything that decides what happens to the machine is
///     a pure function of what is already on it, so all of it is asserted here without a keychain, a
///     hosts file or a packet filter — the only parts left to the platform are the four <c>sudo</c>
///     invocations that carry a decision out.
/// </summary>
public sealed class DevHostTests
{
    // ---- the name ----

    [Theory]
    [InlineData("AppName", "appname.test")]
    [InlineData("Contoso.Web", "contoso-web.test")]
    [InlineData("My App Web", "my-app-web.test")]
    [InlineData("Café", "cafe.test")]
    [InlineData("already-lower", "already-lower.test")]
    public void Hostname_is_derived_from_the_project_name(string project, string expected)
    {
        Assert.Equal(expected, DevHostName.From(project));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("...")]
    [InlineData(null)]
    public void A_name_with_no_usable_label_yields_no_host(string? project)
    {
        // Null is "serve on localhost as before" — never an error. Failing to invent a nicer URL must
        // not stop the app from running.
        Assert.Null(DevHostName.From(project));
    }

    [Fact]
    public void Separators_never_lead_or_trail_the_label()
    {
        Assert.Equal("app.test", DevHostName.From("  ..App..  "));
    }

    [Fact]
    public void A_long_name_stays_inside_the_dns_label_limit()
    {
        var host = DevHostName.From(new string('a', 100));

        Assert.NotNull(host);
        Assert.Equal(63, host!.Split('.')[0].Length);
    }

    // ---- /etc/hosts ----

    [Fact]
    public void The_host_is_added_in_a_marked_block()
    {
        var updated = DevHostFiles.AddHost("127.0.0.1\tlocalhost\n", "appname.test");

        Assert.NotNull(updated);
        Assert.Contains(DevHostFiles.HostsBeginMarker, updated, StringComparison.Ordinal);
        Assert.Contains("127.0.0.1\tappname.test", updated, StringComparison.Ordinal);
        Assert.Contains(DevHostFiles.HostsEndMarker, updated, StringComparison.Ordinal);
    }

    [Fact]
    public void Existing_entries_are_left_alone()
    {
        const string Existing = "127.0.0.1\tlocalhost\n255.255.255.255\tbroadcasthost\n";

        var updated = DevHostFiles.AddHost(Existing, "appname.test");

        Assert.NotNull(updated);
        Assert.StartsWith(Existing, updated, StringComparison.Ordinal);
    }

    /// <summary>
    ///     The property the whole feature rests on: a machine that is already set up produces no change,
    ///     so <c>rask dev</c> asks for a password once rather than on every run.
    /// </summary>
    [Fact]
    public void A_host_that_is_already_there_needs_no_write()
    {
        var first = DevHostFiles.AddHost("127.0.0.1\tlocalhost\n", "appname.test");

        Assert.Null(DevHostFiles.AddHost(first!, "appname.test"));
    }

    [Fact]
    public void A_second_project_joins_the_same_block()
    {
        var first = DevHostFiles.AddHost("127.0.0.1\tlocalhost\n", "one.test");
        var second = DevHostFiles.AddHost(first!, "two.test");

        Assert.NotNull(second);
        Assert.Equal(["one.test", "two.test"], DevHostFiles.ManagedHosts(second!));

        // One block, not one per project.
        Assert.Equal(1, CountOccurrences(second!, DevHostFiles.HostsBeginMarker));
    }

    [Fact]
    public void Removing_the_block_restores_the_original_file()
    {
        const string Original = "127.0.0.1\tlocalhost\n";

        var added = DevHostFiles.AddHost(Original, "appname.test");

        Assert.Equal(Original, DevHostFiles.RemoveBlock(added!));
    }

    [Fact]
    public void Removing_a_block_that_is_not_there_is_no_change()
    {
        Assert.Null(DevHostFiles.RemoveBlock("127.0.0.1\tlocalhost\n"));
    }

    // ---- pf ----

    [Fact]
    public void Port_443_is_redirected_to_the_listener()
    {
        var rules = DevHostFiles.PfRules(5001);

        Assert.NotNull(rules);
        Assert.Contains("port 443 -> 127.0.0.1 port 5001", rules, StringComparison.Ordinal);

        // Loopback only: a rule on the real interface would put the dev app on the network.
        Assert.Contains("on lo0", rules, StringComparison.Ordinal);
        Assert.EndsWith("\n", rules, StringComparison.Ordinal);
    }

    [Fact]
    public void Nothing_is_redirected_when_the_app_already_listens_on_443()
    {
        Assert.Null(DevHostFiles.PfRules(443));
    }

    /// <summary>
    ///     Port 80 is deliberately not mapped. The dev host is HTTPS end to end, and pointing 80 at the
    ///     TLS port would answer a plain request with a handshake — which reads as a broken connection
    ///     rather than as "use https".
    /// </summary>
    [Fact]
    public void Plaintext_is_never_redirected()
    {
        Assert.DoesNotContain("port 80 ", DevHostFiles.PfRules(5001)!, StringComparison.Ordinal);
    }

    // ---- certificates ----

    [Fact]
    public void The_authority_is_a_certificate_authority()
    {
        using var authority = DevCertificates.Load(DevCertificates.CreateAuthority(DateTimeOffset.Now));

        var constraints = authority.Extensions.OfType<X509BasicConstraintsExtension>().Single();

        Assert.True(constraints.CertificateAuthority);

        // pathLength 0: it may sign server certificates and nothing that is itself an authority.
        Assert.True(constraints.HasPathLengthConstraint);
        Assert.Equal(0, constraints.PathLengthConstraint);
    }

    [Fact]
    public void An_issued_certificate_covers_the_hostname()
    {
        var certificate = Issue("appname.test");

        using var loaded = DevCertificates.Load(certificate);

        // MatchesHostname reads the subject alternative name, which is what browsers actually check —
        // a common name alone has not been accepted by Chrome or Safari for years.
        Assert.True(loaded.MatchesHostname("appname.test"));
        Assert.False(loaded.MatchesHostname("other.test"));
    }

    [Fact]
    public void An_issued_certificate_is_for_server_authentication()
    {
        using var loaded = DevCertificates.Load(Issue("appname.test"));

        var usage = loaded.Extensions.OfType<X509EnhancedKeyUsageExtension>().Single();

        Assert.Contains(usage.EnhancedKeyUsages.Cast<Oid>(), oid => oid.Value == "1.3.6.1.5.5.7.3.1");
    }

    [Fact]
    public void An_issued_certificate_chains_to_the_authority()
    {
        var authority = DevCertificates.CreateAuthority(DateTimeOffset.Now);
        var server = DevCertificates.IssueServerCertificate(authority, "appname.test", DateTimeOffset.Now);

        using var root = DevCertificates.Load(authority);
        using var leaf = DevCertificates.Load(server);

        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Add(root);
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;

        Assert.True(chain.Build(leaf), string.Join("; ", chain.ChainStatus.Select(s => s.StatusInformation.Trim())));
    }

    [Fact]
    public void An_issued_certificate_never_outlives_its_authority()
    {
        var authority = DevCertificates.CreateAuthority(DateTimeOffset.Now);

        using var root = DevCertificates.Load(authority);
        using var leaf = DevCertificates.Load(
            DevCertificates.IssueServerCertificate(authority, "appname.test", DateTimeOffset.Now));

        Assert.True(leaf.NotAfter <= root.NotAfter);
    }

    [Fact]
    public void A_missing_certificate_needs_issuing()
    {
        Assert.True(DevCertificates.NeedsReissue(null, "appname.test", DateTimeOffset.Now));
    }

    [Fact]
    public void A_fresh_certificate_does_not()
    {
        Assert.False(DevCertificates.NeedsReissue(Issue("appname.test"), "appname.test", DateTimeOffset.Now));
    }

    [Fact]
    public void A_certificate_for_another_name_needs_reissuing()
    {
        Assert.True(DevCertificates.NeedsReissue(Issue("other.test"), "appname.test", DateTimeOffset.Now));
    }

    [Fact]
    public void A_certificate_near_expiry_is_replaced_before_it_lapses()
    {
        var certificate = Issue("appname.test");

        using var loaded = DevCertificates.Load(certificate);
        var justInsideTheWindow = loaded.NotAfter - DevCertificates.RenewalWindow + TimeSpan.FromHours(1);

        Assert.True(DevCertificates.NeedsReissue(certificate, "appname.test", new DateTimeOffset(justInsideTheWindow)));
    }

    /// <summary>
    ///     The authority is checked with a null hostname. It has no subject alternative name, so asking
    ///     whether it "matches a hostname" would answer no every time — re-minting the authority on
    ///     every run, and asking for a password with it.
    /// </summary>
    [Fact]
    public void The_authority_is_not_checked_against_a_hostname()
    {
        var authority = DevCertificates.CreateAuthority(DateTimeOffset.Now);

        Assert.False(DevCertificates.NeedsReissue(authority, hostname: null, DateTimeOffset.Now));
    }

    [Fact]
    public void An_unreadable_certificate_is_replaced_rather_than_served()
    {
        var corrupt = new DevCertificate("-----BEGIN CERTIFICATE-----\nnot a certificate\n-----END CERTIFICATE-----", "");

        Assert.True(DevCertificates.NeedsReissue(corrupt, "appname.test", DateTimeOffset.Now));
    }

    // ---- the store ----

    [Fact]
    public void The_authority_survives_a_round_trip()
    {
        using var directory = new TemporaryDirectory();
        var store = new DevHostStore(directory.Path);

        Assert.Null(store.ReadAuthority());

        var authority = DevCertificates.CreateAuthority(DateTimeOffset.Now);
        store.WriteAuthority(authority);

        Assert.Equal(authority, store.ReadAuthority());
    }

    [Fact]
    public void A_stored_private_key_is_readable_only_by_its_owner()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var directory = new TemporaryDirectory();
        var store = new DevHostStore(directory.Path);

        store.WriteCertificate("appname.test", Issue("appname.test"));

        // Anyone who can read this key can mint a certificate the developer's browser trusts.
        Assert.Equal(
            UnixFileMode.UserRead | UnixFileMode.UserWrite,
            File.GetUnixFileMode(store.KeyPath("appname.test")));
    }

    /// <summary>
    ///     The seam the rest of the file cannot reach: the files actually on disk, loaded the way a
    ///     server loads them, completing a real TLS handshake that a client checking the hostname
    ///     against the authority accepts.
    /// </summary>
    /// <remarks>
    ///     Everything else here asserts a certificate's contents, which a mismatched key pair or a
    ///     mis-encoded private key would sail straight through — the handshake is where that shows up.
    ///     <c>SslStream</c> rather than Kestrel because it is the same <c>CreateFromPem</c> load and the
    ///     same negotiation, without dragging the ASP.NET host into the CLI's test project.
    /// </remarks>
    [Fact]
    public async Task The_stored_certificate_serves_a_real_tls_handshake()
    {
        const string Hostname = "appname.test";

        using var directory = new TemporaryDirectory();
        var store = new DevHostStore(directory.Path);

        var authority = DevCertificates.CreateAuthority(DateTimeOffset.Now);
        store.WriteAuthority(authority);
        store.WriteCertificate(Hostname, DevCertificates.IssueServerCertificate(authority, Hostname, DateTimeOffset.Now));

        // Loaded from the paths handed to Kestrel, not from the objects minted above.
        using var served = X509Certificate2.CreateFromPemFile(store.CertificatePath(Hostname), store.KeyPath(Hostname));
        using var root = DevCertificates.Load(store.ReadAuthority()!.Value);

        using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;

        var server = Task.Run(async () =>
        {
            using var connection = await listener.AcceptTcpClientAsync();
            await using var tls = new System.Net.Security.SslStream(connection.GetStream(), leaveInnerStreamOpen: false);
            await tls.AuthenticateAsServerAsync(served);
        });

        using var client = new System.Net.Sockets.TcpClient();
        await client.ConnectAsync(System.Net.IPAddress.Loopback, port);

        await using var clientTls = new System.Net.Security.SslStream(
            client.GetStream(),
            leaveInnerStreamOpen: false,
            userCertificateValidationCallback: (_, certificate, chain, errors) =>
            {
                // Exactly what a browser does once the authority is in its trust store: build the chain
                // to that root, and require the name to match.
                Assert.Equal(System.Net.Security.SslPolicyErrors.RemoteCertificateChainErrors, errors);

                chain!.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                chain.ChainPolicy.CustomTrustStore.Add(root);
                chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;

                // Cast rather than copy-constructed: the runtime already hands over an X509Certificate2,
                // and the constructor that would rebuild one from it is obsolete.
                var presented = (X509Certificate2)certificate!;

                return chain.Build(presented) && presented.MatchesHostname(Hostname);
            });

        await clientTls.AuthenticateAsClientAsync(Hostname);
        await server;

        Assert.True(clientTls.IsEncrypted);
    }

    [Fact]
    public void Pf_state_survives_a_round_trip()
    {
        using var directory = new TemporaryDirectory();
        var store = new DevHostStore(directory.Path);

        Assert.Null(store.ReadPfState());

        store.WritePfState("rdr ...", "boot-1");

        Assert.Equal(("rdr ...", "boot-1"), store.ReadPfState());
    }

    [Fact]
    public void An_unreadable_boot_time_still_yields_a_stable_id()
    {
        Assert.False(string.IsNullOrWhiteSpace(DevHostStore.BootId(null)));
        Assert.Equal("{ sec = 1 }", DevHostStore.BootId("  { sec = 1 }  "));
    }

    // ---- the plan ----

    [Fact]
    public void A_plan_with_nothing_to_do_needs_no_password()
    {
        var plan = new DevHostPlan { Hostname = "appname.test" };

        Assert.True(plan.IsSatisfied);
        Assert.False(plan.NeedsPrivilege);
        Assert.Empty(plan.PrivilegedChanges);
    }

    [Fact]
    public void Minting_and_issuing_alone_need_no_password()
    {
        // Both happen inside the developer's own home directory, so neither is something they should be
        // asked to approve.
        var plan = new DevHostPlan { Hostname = "appname.test", MintAuthority = true, IssueCertificate = true };

        Assert.False(plan.IsSatisfied);
        Assert.False(plan.NeedsPrivilege);
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public void Every_machine_change_is_named_before_it_is_made(bool trust, bool hosts, bool port)
    {
        var plan = new DevHostPlan
        {
            Hostname = "appname.test",
            TrustAuthority = trust,
            Hosts = hosts ? "..." : null,
            PortSetup = port ? "..." : null,
            TrustChange = "trust the authority",
            PortChange = "open the port",
            HostsPath = "/etc/hosts",
        };

        Assert.True(plan.NeedsPrivilege);
        Assert.Single(plan.PrivilegedChanges);
    }

    // ---- the platforms ----

    /// <summary>
    ///     The port decision per platform, which is the one that surprises people: only macOS reserves
    ///     ports below 1024 from an ordinary process, so it alone needs a redirect. Asserted on every
    ///     platform, because these are pure properties and getting one wrong would mean Kestrel binding
    ///     a port nothing routes to.
    /// </summary>
    [Fact]
    public void Only_macos_needs_a_port_redirect()
    {
        var (mac, windows, linux) = Platforms();

        // macOS: a high port plus a pf anchor mapping 443 onto it.
        Assert.Equal(5001, mac.HttpsPort);
        Assert.NotNull(mac.PortChange);

        // Windows: binds 443 outright. Nothing to set up, nothing to undo, nothing lost at reboot.
        Assert.Equal(443, windows.HttpsPort);
        Assert.Null(windows.PortChange);

        // Linux: binds 443 too, once one sysctl allows it.
        Assert.Equal(443, linux.HttpsPort);
        Assert.NotNull(linux.PortChange);
    }

    [Fact]
    public async Task Windows_never_asks_for_port_setup()
    {
        var (_, windows, _) = Platforms();

        Assert.Null(await windows.RequiredPortSetupAsync(CancellationToken.None));
    }

    [Fact]
    public void Each_platform_names_its_own_trust_store()
    {
        var (mac, windows, linux) = Platforms();

        Assert.Contains("keychain", mac.TrustChange, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Trusted Roots", windows.TrustChange, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("anchors", linux.TrustChange, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_current_platform_is_the_one_we_are_running_on()
    {
        var platform = DevHostPlatform.Create(new FakeProcessRunner(), new StringConsole(), new DevHostStore(Path.GetTempPath()));

        if (OperatingSystem.IsMacOS())
        {
            Assert.IsType<MacDevHostPlatform>(platform);
        }
        else if (OperatingSystem.IsWindows())
        {
            Assert.IsType<WindowsDevHostPlatform>(platform);
        }
        else if (OperatingSystem.IsLinux())
        {
            Assert.IsType<LinuxDevHostPlatform>(platform);
        }
        else
        {
            // Not a failure: the dev host is absent there, and rask dev serves localhost as before.
            Assert.Null(platform);
        }
    }

    /// <summary>
    ///     All three platforms, constructed on whichever one the suite happens to be running on. Their
    ///     port and trust-store decisions are pure properties, so every platform's answer is asserted
    ///     everywhere rather than only on the machine that would use it.
    /// </summary>
    private static (MacDevHostPlatform Mac, WindowsDevHostPlatform Windows, LinuxDevHostPlatform Linux) Platforms()
    {
        var process = new FakeProcessRunner();
        var console = new StringConsole();
        var store = new DevHostStore(Path.GetTempPath());

        return (
            new MacDevHostPlatform(process, console, store),
            new WindowsDevHostPlatform(process, console),
            new LinuxDevHostPlatform(process, console));
    }

    private static DevCertificate Issue(string hostname) =>
        DevCertificates.IssueServerCertificate(
            DevCertificates.CreateAuthority(DateTimeOffset.Now), hostname, DateTimeOffset.Now);

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;

        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } =
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "rask-devhost-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, recursive: true);
                }
            }
            catch (IOException)
            {
            }
        }
    }
}
