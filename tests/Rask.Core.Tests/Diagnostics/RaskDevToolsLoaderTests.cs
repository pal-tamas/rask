using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Diagnostics.DevTools;

namespace Rask.Core.Tests.Diagnostics;

/// <summary>
///     The host-side loader, from an assembly that does NOT reference <c>Rask.DevTools</c> — the situation of
///     every app built without the package, and of a Release build that stripped it.
/// </summary>
public sealed class RaskDevToolsLoaderTests
{
    [Fact]
    public void Without_the_package_attaching_is_a_no_op()
    {
        var services = new ServiceCollection();

        Assert.False(RaskDevToolsLoader.Attach(services));
        Assert.Empty(services);
    }

    [Fact]
    public void Without_the_package_the_gated_call_is_a_no_op_whatever_the_switch_says()
    {
        var services = new ServiceCollection();

        Assert.False(RaskDevToolsLoader.TryAttach(services));
        Assert.Empty(services);
    }
}
