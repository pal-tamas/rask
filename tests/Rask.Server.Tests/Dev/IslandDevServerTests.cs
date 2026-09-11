using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Rask.Server.Dev;
using Rask.Server.Prerender;

namespace Rask.Server.Tests.Dev;

/// <summary>
///     Islands under an editor's F5: the app starts their Vite dev server itself, and the page imports from
///     wherever that server is — or from <c>rask dev</c>'s, when <c>rask dev</c> started one.
/// </summary>
public sealed class IslandDevServerTests
{
    private static readonly System.Reflection.Assembly DevSessionBuild = typeof(IslandDevServerTests).Assembly;

    [Fact]
    public void A_dev_session_build_registers_one_server_reachable_both_ways()
    {
        var services = new ServiceCollection();

        IslandDevServer.Register(services, DevSessionBuild);
        IslandDevServer.Register(services, DevSessionBuild);

        Assert.Single(services, d => d.ServiceType == typeof(IslandDevServer));
        Assert.Single(services, d => d.ServiceType == typeof(IHostedService));

        services.AddSingleton<IHostEnvironment>(new TestEnvironment());
        services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(NullLogger<>));
        using var provider = services.BuildServiceProvider();

        // The page endpoint asks the same instance the host started, or it would never learn the URL.
        Assert.Same(provider.GetRequiredService<IslandDevServer>(), provider.GetRequiredService<IHostedService>());
    }

    [Fact]
    public void An_ordinary_build_registers_nothing()
    {
        var services = new ServiceCollection();

        IslandDevServer.Register(services, typeof(object).Assembly);

        Assert.Empty(services);
    }

    [Fact]
    public void The_page_imports_islands_from_the_server_the_app_started()
    {
        var server = new IslandDevServer(new TestEnvironment(), NullLogger<IslandDevServer>.Instance);
        server.ServingFrom("http://localhost:5174");

        var services = new ServiceCollection().AddSingleton(server).BuildServiceProvider();

        if (Environment.GetEnvironmentVariable("RASK_ISLANDS_DEV") is null)
        {
            Assert.Equal("http://localhost:5174", PageDocument.IslandsDevUrl(services));
        }

        server.ServingFrom(null);
        if (Environment.GetEnvironmentVariable("RASK_ISLANDS_DEV") is null)
        {
            Assert.Null(PageDocument.IslandsDevUrl(services));
        }
    }

    [Fact]
    public void Without_a_server_there_is_nothing_to_import_from()
    {
        using var services = new ServiceCollection().BuildServiceProvider();

        if (Environment.GetEnvironmentVariable("RASK_ISLANDS_DEV") is null)
        {
            Assert.Null(PageDocument.IslandsDevUrl(services));
        }
    }

    [Fact]
    public void The_islands_address_is_stamped_only_in_development()
    {
        var limits = RaskServerLimits.From(new RaskServerOptions());
        const string html = "<html><body></body></html>";

        Assert.Contains(
            "data-rask-islands-dev=\"http://localhost:5174\"",
            PageDocument.Live(html, "s1", limits, dev: true, "http://localhost:5174", devTools: null),
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "data-rask-islands-dev",
            PageDocument.Live(html, "s1", limits, dev: false, "http://localhost:5174", devTools: null),
            StringComparison.Ordinal);
    }

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "IslandsApp";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
