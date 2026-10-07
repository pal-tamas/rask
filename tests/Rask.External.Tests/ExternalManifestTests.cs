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

    [Fact]
    public void An_island_is_loaded_from_the_pages_own_origin_only()
    {
        // #1183: the `manifest` attribute is markup, and what it leads to is a module import. A
        // sanitizer that keeps unknown elements would otherwise let user HTML name any script.
        var doc = NodeFixture.Run("ExternalManifestFixture");
        if (doc is null)
        {
            return;
        }

        var refusals = string.Join('\n', Strings(doc.Value, "refusals"));

        Assert.DoesNotContain("https://elsewhere.example/manifest.json", Strings(doc.Value, "fetched"));
        Assert.Equal(Strings(doc.Value, "mountedNames"), Strings(doc.Value, "mountedAfterRefusals"));
        Assert.Contains("refusing the manifest at https://elsewhere.example/manifest.json", refusals, StringComparison.Ordinal);
        Assert.Contains("refusing the chunk https://elsewhere.example/Leak.js", refusals, StringComparison.Ordinal);
        Assert.Contains("refusing the chunk data:text/javascript", refusals, StringComparison.Ordinal);
        Assert.Contains("refusing the chunk http://localhost:5174/", refusals, StringComparison.Ordinal);
    }

    [Fact]
    public void A_chunk_on_the_island_dev_server_loads_once_the_page_names_that_server()
    {
        var doc = NodeFixture.Run("ExternalManifestFixture");
        if (doc is null)
        {
            return;
        }

        Assert.Empty(Strings(doc.Value, "devErrors"));
        Assert.Equal(["Live"], Strings(doc.Value, "mountedUnderDev"));
    }

    [Fact]
    public void A_failed_manifest_fetch_is_retried_by_the_next_island()
    {
        // The fetch promise is cached per URL. Kept after it rejected, one 404 in the middle of a deploy
        // failed every island on the page until a reload.
        var doc = NodeFixture.Run("ExternalManifestFixture");
        if (doc is null)
        {
            return;
        }

        var mounted = Strings(doc.Value, "mountedAfterRetry");

        Assert.Equal(["Late"], mounted);
        Assert.Equal(2, doc.Value.GetProperty("flakyFetches").GetInt32());
    }

    [Fact]
    public void A_failed_manifest_fetch_names_its_url()
    {
        var doc = NodeFixture.Run("ExternalManifestFixture");
        if (doc is null)
        {
            return;
        }

        var failure = Assert.Single(Strings(doc.Value, "manifestFailures"));

        Assert.Contains("the manifest at https://app.test/_content/Flaky/manifest.json", failure, StringComparison.Ordinal);
        Assert.Contains("HTTP 404", failure, StringComparison.Ordinal);
        Assert.Contains("check that the deploy published it", failure, StringComparison.Ordinal);
    }

    [Fact]
    public void A_dev_manifest_loads_the_hmr_client_before_the_chunk()
    {
        // A WASM app's page is a static file, so nothing stamps the dev server on <body>: the dev
        // manifest names it under `$dev` instead, which is not an island and cannot be asked for as one.
        var doc = NodeFixture.Run("ExternalManifestFixture");
        if (doc is null)
        {
            return;
        }

        var imported = Strings(doc.Value, "importedByDevManifest");
        var client = Array.IndexOf(imported, "http://localhost:5174/@vite/client");
        var chunk = Array.IndexOf(imported, "http://localhost:5174/@fs/app/Hot.entry.ts");

        Assert.True(client >= 0 && client < chunk, "imported: " + string.Join(", ", imported));
        Assert.Equal(["Hot"], Strings(doc.Value, "mountedByDevManifest"));
        var error = Assert.Single(Strings(doc.Value, "devManifestErrors"));
        Assert.Contains("'$dev' is not in the manifest", error, StringComparison.Ordinal);
    }

    [Fact]
    public void A_dev_manifest_naming_a_non_loopback_server_is_ignored()
    {
        var doc = NodeFixture.Run("ExternalManifestFixture");
        if (doc is null)
        {
            return;
        }

        var refusal = Assert.Single(Strings(doc.Value, "remoteRefusals"));

        Assert.Contains("refusing the chunk https://elsewhere.example/Remote.js", refusal, StringComparison.Ordinal);
        Assert.DoesNotContain("https://elsewhere.example/@vite/client", Strings(doc.Value, "importedByDevManifest"));
        Assert.DoesNotContain("https://elsewhere.example/Remote.js", Strings(doc.Value, "importedByDevManifest"));
    }

    [Fact]
    public void A_dev_server_chunk_is_refused_without_a_dev_manifest_or_stamp()
    {
        // `Live` is in the app's own manifest, which names no dev server, and the page is not stamped yet.
        var doc = NodeFixture.Run("ExternalManifestFixture");
        if (doc is null)
        {
            return;
        }

        var refusals = string.Join('\n', Strings(doc.Value, "refusals"));

        Assert.Contains("refusing the chunk http://localhost:5174/@fs/app/Live.entry.ts for 'Live'", refusals, StringComparison.Ordinal);
        Assert.DoesNotContain("Live", Strings(doc.Value, "mountedAfterRefusals"));
    }

    [Fact]
    public void An_injected_base_element_cannot_move_the_default_manifest()
    {
        // A root-relative URL ignores a <base>'s path but takes its origin, and that origin is not the page's.
        var doc = NodeFixture.Run("ExternalManifestFixture");
        if (doc is null)
        {
            return;
        }

        var refusal = Assert.Single(Strings(doc.Value, "baseRefusals"));

        Assert.Contains("refusing the manifest at /_rask/external/manifest.json", refusal, StringComparison.Ordinal);
        Assert.Empty(Strings(doc.Value, "fetchedUnderBase"));
        Assert.Empty(Strings(doc.Value, "mountedUnderBase"));
    }

    private static string?[] Strings(System.Text.Json.JsonElement doc, string property) =>
        [.. doc.GetProperty(property).EnumerateArray().Select(e => e.GetString())];
}
