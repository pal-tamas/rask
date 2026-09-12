using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Diagnostics.DevTools;
using Rask.Server.DevTools;

namespace Rask.DevTools.Tests;

/// <summary>
///     The hosts find the devtools by NAME, so the name is the contract. A rename or a namespace move
///     compiles cleanly everywhere and turns the devtools off in every app, with nothing reporting it.
/// </summary>
public sealed class DevToolsBootstrapTests
{
    [Fact]
    public void The_loader_name_is_the_bootstrap_type()
    {
        var qualified = typeof(DevToolsBootstrap).FullName + ", " + typeof(DevToolsBootstrap).Assembly.GetName().Name;

        Assert.Equal(RaskDevToolsLoader.BootstrapTypeName, qualified);
    }

    [Fact]
    public void Attaching_registers_the_devtools()
    {
        var services = new ServiceCollection();

        Assert.True(RaskDevToolsLoader.Attach(services));
        Assert.Contains(services, d => d.ServiceType == typeof(DevToolsRegistration));
    }

    [Fact]
    public void Attaching_on_the_server_face_registers_the_endpoints_UseRask_maps()
    {
        var services = new ServiceCollection();

        RaskDevToolsLoader.Attach(services);

        Assert.Contains(services, d => d.ServiceType == typeof(IRaskServerDevTools));
    }

    [Fact]
    public void Attaching_twice_registers_once()
    {
        var services = new ServiceCollection();

        RaskDevToolsLoader.Attach(services);
        RaskDevToolsLoader.Attach(services);

        Assert.Single(services, d => d.ServiceType == typeof(DevToolsRegistration));
    }

    [Fact]
    public void The_switch_this_project_asks_for_reaches_the_runtime()
    {
        // Directory.Build.targets imports Rask.DevTools.targets into every project, and this one sets
        // RaskDevTools=true (see the csproj: the probe's hook sites are inert without it, and the unit gate
        // builds Release). So the switch must read true here in EVERY configuration — if it does not, the
        // targets that carry the gate into every app are not being applied at all, and a devtools test that
        // drives a real page would quietly observe nothing.
        //
        // The other half — that a Release build carries no devtools — is not this assembly's to prove, and a
        // project that forces the switch on cannot: the fixture publishes do it, where the targets strip the
        // assembly and then fail the build if any of it survived.
        Assert.True(RaskDevToolsFeature.IsEnabled);
    }
}
