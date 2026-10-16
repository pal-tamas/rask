using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.Endpoints;

// The path base is the process's, and a page rendered beside a host mounted under one carries its prefix in the
// runtime tag. A test that runs in parallel with the others may not mount one: its neighbours' frames would change.
public sealed class PathBaseRunsAloneTests
{
    [Fact]
    public void A_test_that_runs_beside_others_cannot_mount_a_host_under_a_path_base()
    {
        var mount = static () => RaskTestHost.Create<TestApp>(pathBase: "/uj");

        var refused = Assert.Throws<InvalidOperationException>(mount);

        Assert.Contains("DisableParallelization", refused.Message, StringComparison.Ordinal);
    }
}
