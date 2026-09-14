namespace Rask.Storage.Tests;

// #1086: Rask.Core travels only inside the hosts that render components, so the SPA and meta lanes have no copy of it.
// A Storage assembly that referenced Core died before Main on every front-end template the moment MapRaskStorage was
// called. The build still passed, so the compiled assembly is what this reads: a Core type named anywhere in Storage
// puts the reference back.
public sealed class StorageCoreIndependenceTests
{
    [Fact]
    public void The_storage_assembly_references_no_Rask_Core()
    {
        var references = typeof(IFiles).Assembly.GetReferencedAssemblies().Select(a => a.Name).ToArray();

        Assert.DoesNotContain("Rask.Core", references);
    }
}
