using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Rask.Wasm.Hosting.Tests;

/// <summary>
///     The WASM bundle host applies the same host defaults <c>Rask.Server</c>'s <c>AddRask</c> does.
/// </summary>
/// <remarks>
///     <para>
///         This exists because the guarantee was very nearly shipped with a hole in it. The defaults went
///         into <c>Rask.Server.AddRask</c> and the hand-written blocks came out of every template at the
///         same time — but this host calls <c>Rask.Wasm.Hosting</c>'s own <c>AddRask</c>, which does
///         compression and nothing else, and the <c>wasm-hosted</c> template only reaches
///         <c>AddRaskServer</c> when the operator dashboard is switched on. A cookie-authenticated bundle
///         host scaffolded without <c>--ops</c> would have come out with no persisted key ring at all:
///         signed out on every deploy, which is the exact bug the change set out to fix.
///     </para>
///     <para>
///         Every host-level unit test passed while that was true, because they all called the one
///         <c>AddRask</c> that had been fixed. So this asserts the seam rather than the part.
///     </para>
/// </remarks>
public class HostDefaultsTests : IDisposable
{
    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "WasmHostApp";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } =
            new PhysicalFileProvider(AppContext.BaseDirectory);
    }

    private readonly List<string> _dirs = [];

    public void Dispose()
    {
        foreach (var dir in _dirs)
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch (IOException)
            {
                // A temp directory left behind is not worth failing a run over.
            }
        }

        GC.SuppressFinalize(this);
    }

    private ServiceProvider Build(string? keyPath)
    {
        var settings = new Dictionary<string, string?>();
        if (keyPath is not null)
        {
            _dirs.Add(keyPath);
            settings["Rask:DataProtection:KeyPath"] = keyPath;
        }

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        services.AddSingleton<IHostEnvironment>(new TestEnvironment());
        services.AddDataProtection();
        services.AddRask();
        return services.BuildServiceProvider();
    }

    private string NewDir() => Path.Combine(Path.GetTempPath(), $"rask-wasmhost-keys-{Guid.NewGuid():N}");

    [Fact]
    public void AddRask_persists_the_key_ring()
    {
        var keyPath = NewDir();

        using var provider = Build(keyPath);
        var options = provider.GetRequiredService<IOptions<KeyManagementOptions>>().Value;

        var repository = Assert.IsType<FileSystemXmlRepository>(options.XmlRepository);
        Assert.Equal(Path.GetFullPath(keyPath), Path.GetFullPath(repository.Directory.FullName));
    }

    [Fact]
    public void AddRask_pins_the_application_discriminator()
    {
        using var provider = Build(NewDir());

        Assert.Equal(
            "WasmHostApp",
            provider.GetRequiredService<IOptions<DataProtectionOptions>>().Value.ApplicationDiscriminator);
    }

    [Fact]
    public void AddRask_budgets_the_shutdown_and_stops_services_concurrently()
    {
        using var provider = Build(keyPath: null);
        var options = provider.GetRequiredService<IOptions<HostOptions>>().Value;

        Assert.True(options.ServicesStopConcurrently);
        Assert.True(options.ShutdownTimeout < TimeSpan.FromSeconds(20),
            $"the host budgets {options.ShutdownTimeout.TotalSeconds}s against a 20s SIGKILL");
    }

    [Fact]
    public void Calling_it_twice_registers_one_of_each()
    {
        // TryAddEnumerable keys on the implementation type, so a repeated call must not stack duplicate
        // setups. They are idempotent either way, but a growing options pipeline is how a cheap default
        // turns into a slow startup nobody can explain.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton<IHostEnvironment>(new TestEnvironment());

        // The double call is the subject of the test, so RASK060 — which reports exactly this shape, and
        // did report it here — is suppressed for the two lines that mean to do it.
#pragma warning disable RASK060
        services.AddRask();
        services.AddRask();
#pragma warning restore RASK060

        Assert.Single(
            services,
            d => d.ServiceType == typeof(IConfigureOptions<HostOptions>)
                 && d.ImplementationType?.Name == "RaskShutdownDefaults");
    }
}
