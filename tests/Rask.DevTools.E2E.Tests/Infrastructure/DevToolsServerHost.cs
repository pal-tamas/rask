using System.Reflection.Metadata;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Rask.Core;
using Rask.Server;

namespace Rask.DevTools.E2E.Tests.Infrastructure;

/// <summary>
///     A Rask.Server app in Development on a loopback port, in this process, with the devtools on — what a developer's
///     <c>dotnet run</c> of a Debug build serves.
/// </summary>
/// <remarks>
///     <para>
///         The devtools install one probe for the whole process, so two of these must never run at once: every journey
///         class is in <see cref="DevToolsHostCollection" />, which does not run alongside itself.
///     </para>
///     <para>
///         Loopback on purpose: the panel refuses a request from anywhere else, and a browser on this machine reaching
///         <c>127.0.0.1</c> is exactly the case it admits.
///     </para>
/// </remarks>
internal sealed class DevToolsServerHost : IAsyncDisposable
{
    private readonly WebApplication _app;

    private DevToolsServerHost(WebApplication app, string baseUrl)
    {
        _app = app;
        BaseUrl = baseUrl;
    }

    public string BaseUrl { get; }

    public static async Task<DevToolsServerHost> StartAsync<TApp>()
        where TApp : Component
    {
        DevToolsGate.AssertOn();

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddRouting();
        builder.Services.AddRask(configureServer: o => o.ShutdownDrainTimeout = TimeSpan.FromMilliseconds(200));

        var app = builder.Build();
        app.UseRouting();
        app.UseWebSockets();
        app.UseRask<TApp>();
        await app.StartAsync();

        var address = app.Services.GetRequiredService<IServer>().Features
            .Get<IServerAddressesFeature>()!.Addresses.First();
        return new DevToolsServerHost(app, address.TrimEnd('/'));
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}

/// <summary>What must hold for a journey to mean anything, checked before any browser opens.</summary>
internal static class DevToolsGate
{
    internal static void AssertOn()
    {
        // The switch a Debug build writes into this assembly's runtimeconfig; a Release build strips the devtools.
        if (!AppContext.TryGetSwitch("Rask.DevTools.IsEnabled", out var enabled) || !enabled)
        {
            throw new InvalidOperationException(
                "The devtools are off in this build: Rask.DevTools.E2E.Tests must be built Debug. Run it through "
                + "scripts/run-devtools-e2e-local.sh.");
        }
    }

    /// <summary>The runtime's dev-error overlay only exists where assemblies can be updated in place.</summary>
    internal static void AssertOverlayAvailable()
    {
        if (!MetadataUpdater.IsSupported)
        {
            throw new InvalidOperationException(
                "The runtime's dev-error overlay needs DOTNET_MODIFIABLE_ASSEMBLIES=debug in the test process's "
                + "environment, which scripts/run-devtools-e2e-local.sh sets.");
        }
    }
}

/// <summary>Every journey that starts a devtools host, run one at a time: the devtools' probe is process-wide.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class DevToolsHostCollection : ICollectionFixture<Site.E2E.Tests.Infrastructure.PlaywrightFixture>
{
    public const string Name = "Rask DevTools hosts (process-wide probe)";
}
