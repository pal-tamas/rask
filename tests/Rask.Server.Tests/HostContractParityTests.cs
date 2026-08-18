using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests;

// The Server end of the cross-host parity gate. RaskHostContracts.All is the set Rask.Core promises resolves
// on every host; this asserts AddRask actually serves all of it. The sibling tests in Rask.Wasm.Tests and
// Rask.Native.Tests make the identical assertion against their own bootstraps, so a contract can only be
// added to Core once every host can serve it.
//
// Resolution, not registration: a descriptor whose own dependencies are missing looks registered and still
// throws at the injection site, which is exactly how the WASM host shipped an IAuthSignIn that needed an
// HttpClient nobody registered. Resolving is what catches that.
[Collection("HostEnvironment")]
public sealed class HostContractParityTests
{
    [Fact]
    public void AddRask_ResolvesEveryCoreHostContract()
    {
        using var host = RaskTestHost.Create<NoOpApp>();
        // Most of these are scoped (one per live session), so they need a scope rather than the root provider.
        using var scope = host.Services.CreateScope();

        var missing = RaskHostContracts.All
            .Where(t => scope.ServiceProvider.GetService(t) is null)
            .Select(t => t.Name)
            .Order()
            .ToList();

        Assert.Empty(missing);
    }
}
