using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Rask.Spa.Hosting.Tests;

/// <summary>
///     The SPA host applies the same host defaults <c>Rask.Server</c>'s <c>AddRask</c> does.
/// </summary>
/// <remarks>
///     The sibling of <c>Rask.Wasm.Hosting.Tests.HostDefaultsTests</c>, and here for the same reason: the
///     defaults landed in <c>Rask.Server.AddRask</c> while the hand-written blocks came out of every
///     template, and this host calls <c>AddRaskSpaHost</c>, which did compression and nothing else. A SPA
///     host serves a bundle rather than rendering components, but it is still the process holding the auth
///     cookie and still the one the deploy SIGKILLs.
/// </remarks>
public class HostDefaultsTests : IDisposable
{
    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "SpaHostApp";
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
        services.AddRaskSpaHost();
        return services.BuildServiceProvider();
    }

    private string NewDir() => Path.Combine(Path.GetTempPath(), $"rask-spahost-keys-{Guid.NewGuid():N}");

    [Fact]
    public void AddRaskSpaHost_persists_the_key_ring()
    {
        var keyPath = NewDir();

        using var provider = Build(keyPath);
        var options = provider.GetRequiredService<IOptions<KeyManagementOptions>>().Value;

        var repository = Assert.IsType<FileSystemXmlRepository>(options.XmlRepository);
        Assert.Equal(Path.GetFullPath(keyPath), Path.GetFullPath(repository.Directory.FullName));
    }

    [Fact]
    public void AddRaskSpaHost_pins_the_application_discriminator()
    {
        using var provider = Build(NewDir());

        Assert.Equal(
            "SpaHostApp",
            provider.GetRequiredService<IOptions<DataProtectionOptions>>().Value.ApplicationDiscriminator);
    }

    [Fact]
    public void AddRaskSpaHost_budgets_the_shutdown_and_stops_services_concurrently()
    {
        using var provider = Build(keyPath: null);
        var options = provider.GetRequiredService<IOptions<HostOptions>>().Value;

        Assert.True(options.ServicesStopConcurrently);
        Assert.True(options.ShutdownTimeout < TimeSpan.FromSeconds(20),
            $"the host budgets {options.ShutdownTimeout.TotalSeconds}s against a 20s SIGKILL");
    }

    [Fact]
    public void With_nowhere_durable_to_write_the_development_key_ring_is_left_alone()
    {
        // Same guard as the other hosts: no configured path and no /data means Rask declines to choose,
        // so `dotnet run` keeps ASP.NET's per-user ring. Asserted on the discriminator, because ASP.NET's
        // own development default is itself a FileSystemXmlRepository and a type check would prove nothing.
        using var provider = Build(keyPath: null);

        Assert.NotEqual(
            "SpaHostApp",
            provider.GetRequiredService<IOptions<DataProtectionOptions>>().Value.ApplicationDiscriminator);
    }
}
