using System.Text.Json;
using System.Text.RegularExpressions;

namespace Rask.Cli.E2E.Tests;

/// <summary>
///     Island hot reload, for real, on both hosts: an app scaffolded with <c>--islands react</c>, run the way
///     <c>rask dev</c> runs it, its islands served by the Vite dev server <c>rask dev</c> starts beside it.
/// </summary>
/// <remarks>
///     <para>
///         <b>What is proven, and at which level.</b> The manifest the app serves names the dev server under
///         <c>$dev</c> (all a WASM app's static page has to go on) and points the island's entry at it; the
///         entry and the island module load from the dev server with the CORS answer a page on the app's
///         loopback origin needs; and editing the island's <c>.tsx</c> on disk is pushed down Vite's HMR
///         socket as an update for that module, after which the dev server serves the edited source. That is
///         the whole of Rask's half of the loop: from <c>@vite/client</c> on, the browser and React Fast
///         Refresh take over, and they are upstream's. Like <see cref="WatchHotReloadE2ETests" /> this
///         drives sockets and HTTP, not a browser — the HMR socket here is the one <c>@vite/client</c>
///         would open, token and all.
///     </para>
///     <para>
///         <b>Gated with the watch cases</b> (<c>RASK_WATCH_E2E=1</c>, in their collection, matched by the same
///         <c>--filter</c>) because it is the same kind of cost: a packed feed, an npm install of the island
///         packages, a dev-session build under a real <c>dotnet watch</c>, and a Vite process.
///     </para>
/// </remarks>
[Collection(WatchHotReloadCollection.Name)]
public sealed partial class IslandWatchHotReloadE2ETests
{
    private const string SkipReason =
        "Island watch gate: set RASK_WATCH_E2E=1 (and RASK_CLI_BUILD_E2E=1) to run it. It packs this commit's " +
        "packages, installs the island packages, and runs a real `dotnet watch` beside a Vite dev server.";

    private const string Island = "ReactCounter";
    private const string IslandSource = "Features/Islands/React/ReactCounter.tsx";
    private const string OriginalLabel = "add {step}";
    private const string EditedLabel = "plus {step}";

    private static bool Enabled =>
        Environment.GetEnvironmentVariable("RASK_WATCH_E2E") == "1" && CliBuildE2E.Enabled;

    [Fact]
    public async Task A_server_app_under_rask_dev_imports_its_island_from_the_dev_server_and_hot_replaces_an_edit()
    {
        Assert.SkipUnless(Enabled, SkipReason);
        await using var app = await WatchApp.StartWithIslandAsync("IslandServer", IslandHost.Server);

        var html = await app.Http.GetStringAsync("/", TestContext.Current.CancellationToken);

        // The Server host's own stamp, beside the manifest's $dev: what rask-external.js trusts first.
        Assert.Contains($"data-rask-islands-dev=\"{DevOrigin(app)}\"", html, StringComparison.Ordinal);
        Assert.Contains($"name=\"{Island}\"", html, StringComparison.Ordinal);
        await AssertTheIslandHotReplacesFromTheDevServerAsync(app);
    }

    [Fact]
    public async Task A_wasm_app_under_rask_dev_finds_the_dev_server_through_its_manifest_and_hot_replaces_an_edit()
    {
        Assert.SkipUnless(Enabled, SkipReason);
        await using var app = await WatchApp.StartWithIslandAsync("IslandWasm", IslandHost.Wasm);

        var html = await app.Http.GetStringAsync("/", TestContext.Current.CancellationToken);

        // A static page: nothing stamps it, so the manifest's $dev is the only way it learns of the server.
        Assert.DoesNotContain("data-rask-islands-dev", html, StringComparison.Ordinal);
        await AssertTheIslandHotReplacesFromTheDevServerAsync(app);
    }

    /// <summary>The manifest, the module load and the HMR push — the same claims on either host.</summary>
    private static async Task AssertTheIslandHotReplacesFromTheDevServerAsync(WatchApp app)
    {
        var ct = TestContext.Current.CancellationToken;
        var dev = DevOrigin(app);
        var page = $"http://127.0.0.1:{app.Port}";

        // (a) The manifest the APP serves, at the URL the client runtime fetches it from.
        using var manifest = JsonDocument.Parse(
            await app.Http.GetStringAsync("/_rask/external/manifest.json", ct));
        Assert.Equal(dev, manifest.RootElement.GetProperty("$dev").GetString());
        var entry = manifest.RootElement.GetProperty(Island).GetString()!;
        Assert.StartsWith(dev + "/@fs/", entry, StringComparison.Ordinal);
        Assert.EndsWith(Island + ".entry.ts", entry, StringComparison.Ordinal);

        // (b) The entry loads from the dev server, and the CORS rule admits the page's loopback origin —
        // and only a loopback one: /@fs/ serves files off the developer's disk.
        using var admitted = await GetWithOriginAsync(app, entry, page);
        Assert.Equal(System.Net.HttpStatusCode.OK, admitted.StatusCode);
        Assert.Equal(page, AllowedOrigin(admitted));
        using var refused = await GetWithOriginAsync(app, entry, "https://example.com");
        Assert.Null(AllowedOrigin(refused));

        // The island module itself, as the entry imports it — which also puts it in Vite's module graph, the
        // way a browser loading the page does. Vite pushes updates only for modules something has loaded.
        var module = IslandImport(await admitted.Content.ReadAsStringAsync(ct), entry);
        var before = await app.Islands!.Http.GetStringAsync(module, ct);
        Assert.Contains("add ", before, StringComparison.Ordinal);
        Assert.DoesNotContain("plus ", before, StringComparison.Ordinal);

        // (c) The edit, saved over the .tsx, arrives on the HMR socket as an update for that module.
        using var hmr = await app.Islands.ConnectHmrAsync(app, page);
        app.ReplaceInFile(IslandSource, OriginalLabel, EditedLabel);

        var frames = await app.ReadFramesUntilAsync(
            hmr,
            f => f.Contains("\"full-reload\"", StringComparison.Ordinal)
                 || (f.Contains("\"update\"", StringComparison.Ordinal)
                     && f.Contains(Island + ".tsx", StringComparison.Ordinal)),
            TimeSpan.FromSeconds(60));

        // Hot-replaced, not reloaded: React Fast Refresh makes the module its own boundary, which is what
        // keeps the island's state across the edit.
        var update = frames[^1];
        Assert.True(
            update.Contains("\"js-update\"", StringComparison.Ordinal)
            && update.Contains(Island + ".tsx", StringComparison.Ordinal),
            $"the edit was not hot-replaced. HMR frames:\n{string.Join("\n", frames)}{app.Log}{app.Islands.Log}");

        var after = await app.Islands.Http.GetStringAsync(module, ct);
        Assert.Contains("plus ", after, StringComparison.Ordinal);
    }

    /// <summary>The dev server's origin, as the manifest and the body stamp spell it: no trailing slash.</summary>
    private static string DevOrigin(WatchApp app) => app.Islands!.Origin.GetLeftPart(UriPartial.Authority);

    private static async Task<HttpResponseMessage> GetWithOriginAsync(WatchApp app, string url, string origin)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Origin", origin);
        return await app.Islands!.Http.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static string? AllowedOrigin(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Access-Control-Allow-Origin", out var values) ? values.Single() : null;

    /// <summary>The island module the transformed entry imports, as a URL on the dev server.</summary>
    private static Uri IslandImport(string entryModule, string entry)
    {
        var import = ImportOfTheIsland().Match(entryModule);
        Assert.True(import.Success, $"the entry module served for {entry} imports no {Island}.tsx:\n{entryModule}");
        return new Uri(new Uri(entry), import.Groups["path"].Value);
    }

    [GeneratedRegex("""["'](?<path>[^"']*ReactCounter\.tsx[^"']*)["']""")]
    private static partial Regex ImportOfTheIsland();
}
