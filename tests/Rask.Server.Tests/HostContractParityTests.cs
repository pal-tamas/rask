using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Core.Globalization;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests;

// The Server end of the cross-host parity gate. RaskHostContracts.All is the set Rask.Core promises resolves
// on every host; this asserts AddRask actually serves all of it. The sibling test in Rask.Wasm.Tests makes
// the identical assertion against its own bootstrap, so a contract can only be added to Core once every
// host can serve it.
//
// Resolution, not registration: a descriptor whose own dependencies are missing looks registered and still
// throws at the injection site, which is exactly how the WASM host shipped an IAuthSignIn that needed an
// HttpClient nobody registered. Resolving is what catches that.
[Collection("HostEnvironment")]
public sealed class HostContractParityTests
{
    [Fact]
    public void AddRask_resolves_every_core_host_contract()
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

    // The cookie is written through Rask.Web, which Core cannot see, so the host registers it ahead of Core's
    // remember-nothing fallback — a server app keeps a visitor's language across a reload with nothing to write.
    [Fact]
    public void The_server_host_remembers_a_chosen_culture_in_a_cookie()
    {
        using var host = RaskTestHost.Create<NoOpApp>();

        using var scope = host.Services.CreateScope();

        Assert.IsType<Rask.Web.CookieCulturePersistence>(scope.ServiceProvider.GetService<IRaskCulturePersistence>());
    }
}
