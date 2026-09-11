using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Diagnostics.DevTools;
using Rask.DevTools.Probe;
using Rask.Server.Tests.Infrastructure;

namespace Rask.DevTools.Tests.Probe;

/// <summary>
///     Where the probe goes: only a host whose devtools switched on puts one into the process-wide hook, and it takes it
///     back out when it stops, so nothing reports into a host that is gone.
/// </summary>
[Collection(DevToolsHookCollection.Name)]
public sealed class DevToolsProbeInstallTests : IDisposable
{
    public DevToolsProbeInstallTests() => RaskDevToolsHook.Probe = null;

    public void Dispose() => RaskDevToolsHook.Probe = null;

    [Fact]
    public async Task A_development_host_installs_its_probe_and_removes_it_when_it_stops()
    {
        using var host = Host("Development");

        Assert.Same(host.Services.GetRequiredService<DevToolsProbe>(), RaskDevToolsHook.Probe);

        await host.StopAsync();

        Assert.Null(RaskDevToolsHook.Probe);
    }

    [Fact]
    public void A_production_host_installs_nothing()
    {
        using var host = Host("Production");

        Assert.Null(RaskDevToolsHook.Probe);
    }

    private static RaskTestHost Host(string environment) =>
        RaskTestHost.Create<DevToolsTestApp>(
            configureServices: s => RaskDevToolsLoader.Attach(s),
            environment: environment);
}
