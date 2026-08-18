using Rask.Core;
using Rask.Core.Live;
using Rask.Native.Tests.Infrastructure;

namespace Rask.Native.Tests.Session;

// The Native end of the cross-host parity gate — see the sibling tests in Rask.Server.Tests and
// Rask.Wasm.Tests. This one goes through the session harness because the Native host registers the
// browser-API tiers inside RunLocalAsync (after any UsePlatform module, so a native backend wins), not in
// the constructor: a test built from NativeAppHost.CreateDefault().Services alone would be asserting against
// half a container.
//
// This is the gate that was missing. IBrowserFileBackend, IDownloadSink and IAuthSignIn were all absent here
// while Server and WASM served them, so a component shared into a native head lost file uploads silently,
// threw on Navigator.Download, and failed DI on injection of IAuthSignIn.
[Collection("NativeSession")]
public sealed class HostContractParityTests() : ResettingTestBase(LiveDiffMode.Auto)
{
    [Fact]
    public async Task NativeHost_ResolvesEveryCoreHostContract()
    {
        var (app, _, _) = await NativeSessionHarness.NewSessionAsync();

        var missing = RaskHostContracts.All
            .Where(t => app.Services.GetService(t) is null)
            .Select(t => t.Name)
            .Order()
            .ToList();

        Assert.Empty(missing);
    }
}
