namespace Rask.Core.Tests;

// RaskHostContracts is the machine-readable form of "everything in Core works on every host". It is spelled
// out by hand rather than reflected, so that AddCoreBrowserApis can keep its trim-safe generic TryAdd calls.
// This test is what keeps a hand-written list honest: it partitions the Rask.Core.Browser namespace against
// the two declared buckets, so adding a wrapper interface forces an explicit decision — "every host serves
// it" (BrowserApis) or "it's a handle a service returns, not a service" (NonServiceBrowserTypes) — instead of
// the wrapper quietly landing on one host and failing DI on the other two.
public sealed class HostContractCompletenessTests
{
    private const string BrowserNamespace = "Rask.Core.Browser";

    private static IEnumerable<Type> PublicBrowserInterfaces =>
        typeof(RaskHostContracts).Assembly
            .GetExportedTypes()
            .Where(t => t.IsInterface && t.Namespace == BrowserNamespace);

    [Fact]
    public void EveryPublicBrowserInterface_IsEitherARequiredService_OrDeclaredNotAService()
    {
        var declared = RaskHostContracts.BrowserApis
            .Concat(RaskHostContracts.NonServiceBrowserTypes)
            .ToHashSet();

        var undeclared = PublicBrowserInterfaces.Where(t => !declared.Contains(t)).Select(t => t.Name).Order();

        Assert.Empty(undeclared);
    }

    [Fact]
    public void TheTwoBrowserBuckets_DoNotOverlap()
    {
        var overlap = RaskHostContracts.BrowserApis
            .Intersect(RaskHostContracts.NonServiceBrowserTypes)
            .Select(t => t.Name)
            .Order();

        Assert.Empty(overlap);
    }

    // A stale entry is as bad as a missing one: it would make every host's parity test demand a registration
    // for an interface Core no longer exposes, and the only way to go green would be to register a type that
    // isn't there. Catch it here, once, instead of three times over.
    [Fact]
    public void NoDeclaredBrowserContract_HasBeenRemovedFromCore()
    {
        var live = PublicBrowserInterfaces.ToHashSet();

        var stale = RaskHostContracts.BrowserApis
            .Concat(RaskHostContracts.NonServiceBrowserTypes)
            .Where(t => !live.Contains(t))
            .Select(t => t.Name)
            .Order();

        Assert.Empty(stale);
    }

    [Fact]
    public void All_IsHostServicesPlusBrowserApis_WithNoDuplicates()
    {
        Assert.Equal(
            RaskHostContracts.HostServices.Count + RaskHostContracts.BrowserApis.Count,
            RaskHostContracts.All.Count);
        Assert.Equal(RaskHostContracts.All.Count, RaskHostContracts.All.Distinct().Count());
    }
}
