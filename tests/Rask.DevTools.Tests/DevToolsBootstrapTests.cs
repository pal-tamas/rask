using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Diagnostics.DevTools;

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
    public void Attaching_twice_registers_once()
    {
        var services = new ServiceCollection();

        RaskDevToolsLoader.Attach(services);
        RaskDevToolsLoader.Attach(services);

        Assert.Single(services, d => d.ServiceType == typeof(DevToolsRegistration));
    }

    [Fact]
    public void A_debug_build_of_this_repo_carries_the_switch()
    {
        // Directory.Build.targets imports Rask.DevTools.targets into every project, so a Debug build of
        // this test assembly must have written the switch into its runtimeconfig. If this fails, the gate
        // every app depends on is not being applied at all.
#if DEBUG
        Assert.True(RaskDevToolsFeature.IsEnabled);
#else
        Assert.False(RaskDevToolsFeature.IsEnabled);
#endif
    }
}
