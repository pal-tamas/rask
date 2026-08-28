using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Rask.Server.Tests.Security;

/// <summary>
///     <c>AddRask</c> puts the Data Protection key ring somewhere durable when the host has somewhere
///     durable, and leaves the development default alone when it does not.
/// </summary>
/// <remarks>
///     An ephemeral key ring is a silent failure, which is why this is the framework's job rather than a
///     line of scaffolded advice: the default ring lives inside the container, every deploy replaces the
///     container, and everything sealed under the old ring simply stops opening. Auth cookies already
///     issued are rejected — all your signed-in users are signed out — and Rask's own session-resume
///     records become unreadable, so reconnecting clients fall back to a full reload. Nothing logs an
///     error, because from the app's side these are just payloads it cannot unprotect.
/// </remarks>
public class DataProtectionKeyRingTests : IDisposable
{
    // The discriminator is read off IHostEnvironment.ApplicationName, so the double only has to carry a
    // recognisable one; the file provider is never touched by this path.
    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "TestApp";
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

    private string NewDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"rask-keyring-{Guid.NewGuid():N}");
        _dirs.Add(dir);
        return dir;
    }

    private static ServiceProvider Build(string? keyPath)
    {
        var settings = new Dictionary<string, string?>();
        if (keyPath is not null)
        {
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

    [Fact]
    public void A_configured_key_path_becomes_the_key_ring_location()
    {
        var keyPath = NewDir();

        using var provider = Build(keyPath);
        var options = provider.GetRequiredService<IOptions<KeyManagementOptions>>().Value;

        var repository = Assert.IsType<FileSystemXmlRepository>(options.XmlRepository);
        Assert.Equal(Path.GetFullPath(keyPath), Path.GetFullPath(repository.Directory.FullName));
    }

    [Fact]
    public void A_shared_key_ring_gets_a_stable_application_discriminator()
    {
        // Half the fix, and the half that is easy to miss: the default discriminator is derived from the
        // content root, which differs between the build image and the runtime image. Two containers sharing
        // one key ring would still derive different keys from it, so the ring would be shared in name only.
        using var provider = Build(NewDir());

        var options = provider.GetRequiredService<IOptions<DataProtectionOptions>>().Value;

        Assert.Equal("TestApp", options.ApplicationDiscriminator);
    }

    [Fact]
    public void With_nowhere_durable_to_write_the_development_default_is_left_alone()
    {
        // The regression guard that keeps `dotnet run` behaving as it always did. No configured path and
        // (on any machine this suite runs on) no /data volume, so Rask must decline to choose — writing a
        // key ring into the working directory would be worse than the default, not better.
        //
        // Asserted on the DECISION rather than on the resulting repository type: ASP.NET's own development
        // default is itself a FileSystemXmlRepository (the per-user ring under ~/.aspnet), so a type check
        // here would pass whether or not Rask had overridden it — green, and proving nothing.
        var setup = new RaskDataProtectionSetup(
            new ConfigurationBuilder().Build(),
            new TestEnvironment(),
            Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);
        Assert.Null(setup.ResolveKeyPath());

        using var provider = Build(keyPath: null);
        var dp = provider.GetRequiredService<IOptions<DataProtectionOptions>>().Value;

        // NOT asserted as null: ASP.NET's default discriminator is the CONTENT ROOT PATH, which is exactly
        // why pinning it matters when a ring is shared — that path differs between the build image and the
        // runtime image, so two containers over one ring would still derive different keys. Here the check
        // is only that Rask left the default in place rather than stamping the application name on it.
        Assert.NotEqual("TestApp", dp.ApplicationDiscriminator);
    }

    [Fact]
    public void An_explicitly_empty_key_path_is_an_opt_out()
    {
        // The escape hatch for a host that has /data but wants to own its own ring. Empty must mean "leave
        // it alone" rather than "write to the current directory", which is what a bare path check would do.
        var setup = new RaskDataProtectionSetup(
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Rask:DataProtection:KeyPath"] = "" })
                .Build(),
            new TestEnvironment(),
            Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);

        Assert.Null(setup.ResolveKeyPath());
    }

    [Fact]
    public void An_app_that_configures_its_own_ring_after_AddRask_wins()
    {
        // Rask picks a default; it does not overrule a decision. Options setups run in registration order,
        // so the app's own PersistKeysToFileSystem after AddRask is the last writer.
        var raskPath = NewDir();
        var appPath = NewDir();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Rask:DataProtection:KeyPath"] = raskPath })
            .Build());
        services.AddSingleton<IHostEnvironment>(new TestEnvironment());
        services.AddRask();
        services.AddDataProtection().PersistKeysToFileSystem(Directory.CreateDirectory(appPath));

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<KeyManagementOptions>>().Value;

        var repository = Assert.IsType<FileSystemXmlRepository>(options.XmlRepository);
        Assert.Equal(Path.GetFullPath(appPath), Path.GetFullPath(repository.Directory.FullName));
    }
}
