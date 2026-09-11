using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Routing;

namespace Rask.Dashboard.Tests;

/// <summary>
///     <c>AddRaskDashboard</c> mounts the console once, however many times it is called — and only its own
///     mount counts as "already mounted".
/// </summary>
/// <remarks>
///     The guard used to skip on ANY <see cref="RaskMountedApp" /> in the container. A host that mounted a
///     second application before calling <c>AddRaskDashboard</c> therefore lost the console entirely: no
///     error, no warning, just a 404 at <c>/_rask</c>.
/// </remarks>
public sealed class DashboardMountGuardTests
{
    [Fact]
    public async Task Another_application_mounted_first_does_not_take_the_console_off_the_host()
    {
        await using var harness = new DashboardHarness(extra: services =>
            services.AddSingleton(new RaskMountedApp(
                typeof(OtherConsole), "/_other/{**path}", typeof(OtherConsole).Assembly)));

        var mounts = harness.Services.GetServices<RaskMountedApp>().ToArray();

        Assert.Contains(mounts, m => m.Root == typeof(OtherConsole));
        Assert.Contains(mounts, m => m.Root == typeof(RaskDashboardShell));
    }

    [Fact]
    public async Task A_keyed_mount_registered_first_does_not_break_registration()
    {
        // ServiceDescriptor.ImplementationInstance throws on a keyed descriptor, so a guard that reads it
        // without checking IsKeyedService first turns someone else's keyed registration into a startup crash.
        await using var harness = new DashboardHarness(extra: services =>
            services.AddKeyedSingleton("other", new RaskMountedApp(
                typeof(OtherConsole), "/_other/{**path}", typeof(OtherConsole).Assembly)));

        Assert.Single(
            harness.Services.GetServices<RaskMountedApp>(),
            m => m.Root == typeof(RaskDashboardShell));
    }

    [Fact]
    public async Task A_repeated_call_still_mounts_the_console_once()
    {
        await using var harness = new DashboardHarness(extra: services =>
            services.AddRaskDashboard<HarnessDbContext>());

        Assert.Single(
            harness.Services.GetServices<RaskMountedApp>(),
            m => m.Root == typeof(RaskDashboardShell));
    }

    private sealed class OtherConsole;
}
