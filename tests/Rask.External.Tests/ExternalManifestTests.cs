using Rask.TestSupport;

namespace Rask.External.Tests;

// Manifest resolution in the client runtime, driven against the production rask-external.js in node.
//
// #939: an island could not be owned by a shared class library, because the runtime resolved its
// manifest from a hard-coded, app-rooted path. A library's static web assets are served under
// `_content/<PackageId>/`, so a library-built bundle landed where nothing looked for it — and, since
// the fetch was cached in a single promise, the first manifest to load was the only one that could
// exist. A page showing islands from the app AND from a library could never resolve both.
//
// Unlike ExternalRuntimeFixture, the fixture behind these does NOT override the resolver: the
// resolver is what is under test. Only `fetch` is stubbed, and each manifest entry points at a real
// `data:` module node imports for real.
public sealed class ExternalManifestTests
{
    [Fact]
    public void An_island_owned_by_a_library_resolves_through_its_own_manifest()
    {
        var doc = NodeFixture.Run("ExternalManifestFixture");
        if (doc is null)
        {
            return;
        }

        var errors = doc.Value.GetProperty("errors").EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.True(errors.Length == 0, "the runtime reported: " + string.Join(" | ", errors));

        // Both mounted — the app's island from the app's manifest, the library's from the library's.
        // Before the fix the second could not resolve at all: its name is not in the app's manifest,
        // which is the only one the runtime would ever fetch.
        var mounted = doc.Value.GetProperty("mountedNames").EnumerateArray()
            .Select(e => e.GetString())
            .ToArray();

        Assert.Contains("Chart", mounted);
        Assert.Contains("Gauge", mounted);
    }

    [Fact]
    public void Each_manifest_is_fetched_once_however_many_islands_use_it()
    {
        // The cache is keyed by URL rather than being a single promise. Keyed wrongly this is either a
        // request per island (the naive fix) or one manifest for the whole page (the bug).
        var doc = NodeFixture.Run("ExternalManifestFixture");
        if (doc is null)
        {
            return;
        }

        Assert.Equal(1, doc.Value.GetProperty("appFetches").GetInt32());

        // Two library-owned islands on the page, one fetch between them.
        Assert.Equal(1, doc.Value.GetProperty("libFetches").GetInt32());
    }
}
