using System.Text.Json;

namespace Rask.Wasm.Tests.JsInteropRuntime;

// What rask-sw.js answers a same-origin GET with. A file named by its content hash can be served from
// the cache without asking the network — a repeat visit otherwise downloads the whole .NET runtime
// again — and everything else has to stay network-first, because cache-first on a URL whose bytes can
// change is a stale app for ever.
//
// Driven through a Node subprocess against the built Browser/rask-sw.js (see
// ServiceWorkerCacheFixture.ts): which requests reach the network is behaviour, not text.
public sealed class ServiceWorkerCacheTests
{
    // One node run for the whole class: the fixture reports every case in a single JSON line.
    private static readonly Lazy<JsonElement?> Fixture = new(RunFixture);

    [Theory]
    [InlineData("frameworkCached")]
    [InlineData("frameworkUnderPathBase")]
    public void A_cached_fingerprinted_framework_file_is_served_without_touching_the_network(string request)
    {
        var results = Results();

        var result = results.GetProperty(request);

        Assert.Equal("cached", result.GetProperty("served").GetString());
        Assert.Equal(0, result.GetProperty("fetches").GetInt32());
    }

    [Fact]
    public void An_uncached_fingerprinted_framework_file_is_fetched_once_and_stored()
    {
        var results = Results();

        var result = results.GetProperty("frameworkUncached");

        Assert.Equal("network", result.GetProperty("served").GetString());
        Assert.Equal(1, result.GetProperty("fetches").GetInt32());
        Assert.Equal("network", result.GetProperty("stored").GetString());
    }

    [Theory]
    [InlineData("scopedAssetCached")]
    [InlineData("scopedAssetUnderPathBase")]
    public void A_cached_scoped_asset_bundle_is_served_without_touching_the_network(string request)
    {
        var results = Results();

        var result = results.GetProperty(request);

        Assert.Equal("cached", result.GetProperty("served").GetString());
        Assert.Equal(0, result.GetProperty("fetches").GetInt32());
    }

    [Theory]
    [InlineData("indexHtml")]
    [InlineData("mainJs")]
    [InlineData("raskWasmJs")]
    [InlineData("serviceWorker")]
    [InlineData("dotnetJsUnfingerprinted")]
    [InlineData("assemblyUnfingerprinted")]
    [InlineData("assemblyLowercaseTenLetters")]
    [InlineData("fingerprintOutsideFramework")]
    [InlineData("scopedAssetWrongHashLength")]
    [InlineData("scopedAssetSourceMap")]
    public void A_file_whose_name_does_not_carry_its_hash_still_goes_to_the_network_when_cached(string request)
    {
        var results = Results();

        var result = results.GetProperty(request);

        Assert.Equal("network", result.GetProperty("served").GetString());
        Assert.Equal(1, result.GetProperty("fetches").GetInt32());
    }

    [Fact]
    public void A_network_failure_on_an_unfingerprinted_file_still_falls_back_to_the_cache()
    {
        var results = Results();

        var result = results.GetProperty("offlineCached");

        Assert.Equal("cached", result.GetProperty("served").GetString());
        Assert.Equal(1, result.GetProperty("fetches").GetInt32());
    }

    [Fact]
    public void An_offline_navigation_still_falls_back_to_the_cached_shell()
    {
        var results = Results();

        var result = results.GetProperty("offlineNavigation");

        Assert.Equal("shell", result.GetProperty("served").GetString());
    }

    [Fact]
    public void A_network_failure_with_nothing_cached_still_fails_the_request()
    {
        var results = Results();

        var result = results.GetProperty("offlineUncached");

        Assert.Equal("failed", result.GetProperty("served").GetString());
    }

    private static JsonElement Results()
    {
        var results = Fixture.Value;
        Assert.SkipWhen(results is null, "node is not on PATH, so the JS-driven service worker fixture cannot run.");

        return results!.Value;
    }

    private static JsonElement? RunFixture()
    {
        var worker = Path.Combine(RepoRoot(), "src", "Rask.Wasm", "Browser", "rask-sw.js");
        Assert.True(File.Exists(worker), $"Worker missing: {worker} — build src/Rask.Wasm first.");

        return NodeFixture.Run("ServiceWorkerCacheFixture", worker);
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Rask.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException($"Could not locate Rask.slnx above {AppContext.BaseDirectory}");
    }
}
