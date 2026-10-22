using Rask.Core.ScopedAssets;

namespace Rask.Server.Tests;

// The only scoped TypeScript in this run is a library's (Rask.Ui's editor): this assembly has none of its own,
// and no test here has touched Rask.Ui's editor by the time a fresh run asks.
public sealed class ScopedAssetsRegisteredFirstTests
{
    [Fact]
    public void A_librarys_scoped_typescript_is_in_the_bundle_before_any_test_touches_the_library()
    {
        var kind = AssetKind.Js;

        var bundle = ScopedAssetRegistry.GetBundleHash(kind);

        Assert.NotEmpty(bundle);
    }
}
