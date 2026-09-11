using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Rask.DevTools.Server;
using Rask.Hosting.Shared;

namespace Rask.DevTools.Tests;

/// <summary>
///     An app VS Code's F5 launched is served on <c>https://&lt;name&gt;.test</c> exactly when <c>rask dev</c>
///     has already set that name up, and says where the browser should go.
/// </summary>
/// <remarks>
///     Everything that decides is a pure function of the machine, read through <see cref="IDevHostMachine" />,
///     so every case runs here without a hosts file, a certificate store or a port.
/// </remarks>
public sealed class EditorDevSessionTests
{
    private const string Root = "/home/me/.rask";

    private static readonly System.Reflection.Assembly DevSessionBuild = typeof(EditorDevSessionTests).Assembly;

    private static readonly System.Reflection.Assembly OrdinaryBuild = typeof(object).Assembly;

    private static readonly string ManagedBlock =
        "127.0.0.1\tlocalhost\n\n" + DevHostFiles.HostsBeginMarker + "\n127.0.0.1\tshop.test\n" + DevHostFiles.HostsEndMarker + "\n";

    // ---- the session itself ----

    [Fact]
    public void A_dev_session_build_in_development_outside_watch_is_an_editor_session()
    {
        Assert.True(EditorDevSession.IsActive(DevSessionBuild, isDevelopment: true, _ => null));
    }

    [Fact]
    public void Under_rask_dev_the_app_leaves_it_to_the_cli()
    {
        // dotnet watch sets DOTNET_WATCH=1 on the app it runs, and rask dev always runs the app through it.
        Assert.False(EditorDevSession.IsActive(DevSessionBuild, isDevelopment: true, v => v == "DOTNET_WATCH" ? "1" : null));
    }

    [Fact]
    public void An_ordinary_build_or_a_production_host_is_never_one()
    {
        Assert.False(EditorDevSession.IsActive(OrdinaryBuild, isDevelopment: true, _ => null));
        Assert.False(EditorDevSession.IsActive(DevSessionBuild, isDevelopment: false, _ => null));
        Assert.False(EditorDevSession.IsActive(null, isDevelopment: true, _ => null));
    }

    // ---- the .test address ----

    [Fact]
    public void A_name_rask_dev_set_up_is_served()
    {
        var host = Resolve(new FakeMachine());

        Assert.NotNull(host);
        Assert.Equal("shop.test", host!.Hostname);
        Assert.Equal(443, host.Port);
        Assert.Equal(DevHostPaths.CertificatePath(Root, "shop.test"), host.CertificatePath);
        Assert.Equal(DevHostPaths.KeyPath(Root, "shop.test"), host.KeyPath);
        Assert.Equal("https://shop.test", host.Url);
    }

    [Fact]
    public void A_redirected_port_is_named_in_the_url()
    {
        // macOS: the pf redirect from 443 resets on reboot and cannot be checked without root, and the
        // explicit port works whether or not it is there.
        var host = Resolve(new FakeMachine { HttpsPort = DevHostPaths.RedirectedHttpsPort });

        Assert.Equal("https://shop.test:5001", host!.Url);
    }

    [Fact]
    public void Nothing_is_set_up_from_an_editor()
    {
        // F5 has no terminal to ask for a password, so a machine rask dev never prepared stays on localhost.
        Assert.Null(Resolve(new FakeMachine { Files = [] }));
    }

    [Fact]
    public void A_certificate_without_its_key_is_not_enough()
    {
        Assert.Null(Resolve(new FakeMachine { Files = [DevHostPaths.CertificatePath(Root, "shop.test")] }));
        Assert.Null(Resolve(new FakeMachine { Files = [DevHostPaths.KeyPath(Root, "shop.test")] }));
    }

    [Fact]
    public void The_name_has_to_be_in_the_block_rask_dev_owns()
    {
        // An entry somebody wrote by hand elsewhere in the file is not one rask dev manages or keeps.
        Assert.Null(Resolve(new FakeMachine { Hosts = "127.0.0.1\tshop.test\n" }));
        Assert.Null(Resolve(new FakeMachine { Hosts = null }));
    }

    [Fact]
    public void An_islands_project_stays_on_localhost()
    {
        // Islands load their modules from a Vite dev server over plain HTTP, which an HTTPS page may not do.
        var machine = new FakeMachine();
        machine.Files.Add(Path.Combine("/app", "obj", "rask-external", "dev.json"));

        Assert.Null(Resolve(machine));
    }

    [Fact]
    public void A_port_the_kernel_would_refuse_is_never_bound()
    {
        // Linux after a reboot: the sysctl rask dev sets is gone, and binding 443 would fail startup.
        Assert.Null(Resolve(new FakeMachine { UnprivilegedPortStart = 1024 }));
        Assert.NotNull(Resolve(new FakeMachine { UnprivilegedPortStart = 443 }));
    }

    [Fact]
    public void Only_an_editor_session_is_served_on_the_name()
    {
        Assert.Null(Resolve(new FakeMachine(), isDevelopment: false));
        Assert.Null(Resolve(new FakeMachine(), readEnv: v => v == "DOTNET_WATCH" ? "1" : null));
        Assert.Null(Resolve(new FakeMachine(), assembly: OrdinaryBuild));
    }

    // ---- where the browser goes ----

    [Fact]
    public void The_browser_is_sent_to_https_when_the_app_serves_it()
    {
        Assert.Equal(
            "https://localhost:5001",
            EditorDevSessionAnnouncer.PreferredAddress(["http://localhost:5000", "https://localhost:5001"]));
    }

    [Fact]
    public void A_wildcard_bind_becomes_an_address_a_browser_can_open()
    {
        Assert.Equal("http://localhost:5000", EditorDevSessionAnnouncer.PreferredAddress(["http://0.0.0.0:5000"]));
        Assert.Equal("http://localhost:5000", EditorDevSessionAnnouncer.PreferredAddress(["http://[::]:5000"]));
        Assert.Null(EditorDevSessionAnnouncer.PreferredAddress([]));
        Assert.Null(EditorDevSessionAnnouncer.PreferredAddress(null));
    }

    // ---- wiring ----

    [Fact]
    public void A_dev_session_build_registers_the_address_and_the_announcer()
    {
        var services = new ServiceCollection();

        EditorDevSessionServices.Add(services, DevSessionBuild);

        Assert.Contains(services, d => d.ServiceType == typeof(IConfigureOptions<KestrelServerOptions>)
                                       && d.ImplementationType == typeof(EditorDevHostKestrelSetup));
        Assert.Contains(services, d => d.ServiceType == typeof(IHostedService)
                                       && d.ImplementationType == typeof(EditorDevSessionAnnouncer));
    }

    [Fact]
    public void An_ordinary_build_registers_nothing()
    {
        var services = new ServiceCollection();

        EditorDevSessionServices.Add(services, OrdinaryBuild);

        Assert.Empty(services);
    }

    [Fact]
    public void Registering_twice_registers_once()
    {
        var services = new ServiceCollection();

        EditorDevSessionServices.Add(services, DevSessionBuild);
        EditorDevSessionServices.Add(services, DevSessionBuild);

        Assert.Single(services, d => d.ImplementationType == typeof(EditorDevSessionAnnouncer));
        Assert.Single(services, d => d.ImplementationType == typeof(EditorDevHostKestrelSetup));
    }

    [Fact]
    public void The_certificate_rask_dev_wrote_loads_with_its_key()
    {
        // The real PEM pair shape rask dev writes: a certificate file and a PKCS#8 key file beside it.
        var directory = Directory.CreateTempSubdirectory("rask-editor-devhost-");
        try
        {
            using var key = RSA.Create(2048);
            var request = new CertificateRequest("CN=shop.test", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using var issued = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));

            var certificatePath = Path.Combine(directory.FullName, "shop.test.pem");
            var keyPath = Path.Combine(directory.FullName, "shop.test.key");
            File.WriteAllText(certificatePath, issued.ExportCertificatePem());
            File.WriteAllText(keyPath, key.ExportPkcs8PrivateKeyPem());

            using var loaded = EditorDevHostKestrelSetup.LoadCertificate(new EditorDevHost("shop.test", 443, certificatePath, keyPath));

            Assert.True(loaded.HasPrivateKey);
            Assert.Equal(issued.Thumbprint, loaded.Thumbprint);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static EditorDevHost? Resolve(
        FakeMachine machine,
        bool isDevelopment = true,
        Func<string, string?>? readEnv = null,
        System.Reflection.Assembly? assembly = null) =>
        EditorDevHost.Resolve(assembly ?? DevSessionBuild, isDevelopment, "Shop", "/app", readEnv ?? (_ => null), machine);

    private sealed class FakeMachine : IDevHostMachine
    {
        public FakeMachine()
        {
            Files = [DevHostPaths.CertificatePath(Root, "shop.test"), DevHostPaths.KeyPath(Root, "shop.test")];
        }

        public string Root => EditorDevSessionTests.Root;

        public int HttpsPort { get; init; } = DevHostPaths.DirectHttpsPort;

        public int? UnprivilegedPortStart { get; init; }

        public HashSet<string> Files { get; init; }

        public string? Hosts { get; init; } = ManagedBlock;

        public bool FileExists(string path) => Files.Contains(path);

        public string? ReadHosts() => Hosts;
    }
}
