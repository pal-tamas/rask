using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Rask.Site.E2E.Tests.Infrastructure;
using Rask.Wasm.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Rask.Wasm.E2E.Tests;

/// <summary>
///     A WASM app with an island, served under a sub-path. The island's client runtime reads the base off the URL it
///     was loaded from and asks for the manifest and every chunk under it; the island's own navigation, with the app's
///     typed routes from <c>@rask/routes</c>, goes to the page under that base without a reload.
/// </summary>
/// <remarks>
///     The host answers nothing outside the mount, as a host beside another site does, so a runtime that asked the
///     origin root would leave the island empty — and the requests are read back besides, so the failure says where
///     it asked. The site's islands journeys (Rask.Site.E2E.Tests) run at the origin root and cannot see this.
/// </remarks>
public sealed class IslandsUnderASubPathTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    private const string Mount = "/app";

    // The first WASM boot downloads and starts the runtime.
    private static readonly LocatorAssertionsToHaveTextOptions Boot = new() { Timeout = 60_000 };
    private static readonly LocatorAssertionsToHaveTextOptions Settled = new() { Timeout = 15_000 };
    private static readonly PageAssertionsToHaveURLOptions Arrived = new() { Timeout = 15_000 };

    [Fact]
    public async Task An_island_loads_its_runtime_manifest_and_chunk_under_the_apps_sub_path()
    {
        await using var host = await SignInHost.StartAsync(Mount);
        await using var context = await playwright.Browser.NewContextAsync();
        var page = await context.NewPageAsync();

        await page.GotoAsync(host.BaseUrl + Mount + "/islands");

        await Expect(page.Locator("#to-desk")).ToHaveTextAsync("to the desk", Boot);
        var island = host.Requests.Where(IsIslandAsset).ToList();
        Assert.Contains(Mount + "/_content/Rask.External/rask-external.js", island);
        Assert.Contains(Mount + "/_rask/external/manifest.json", island);
        Assert.Contains(island, path => Regex.IsMatch(path, $"^{Mount}/_rask/external/.+\\.js$"));
        Assert.All(island, path => Assert.StartsWith(Mount + "/", path, StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_island_navigates_with_the_apps_typed_routes_under_the_sub_path_without_a_reload()
    {
        await using var host = await SignInHost.StartAsync(Mount);
        await using var context = await playwright.Browser.NewContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync(host.BaseUrl + Mount + "/islands");
        await Expect(page.Locator("#to-desk")).ToHaveTextAsync("to the desk", Boot);
        await page.EvaluateAsync("() => { window.raskProbe = 'kept'; }");

        await page.ClickAsync("#to-desk");

        await Expect(page).ToHaveURLAsync(host.BaseUrl + Mount + "/desk", Arrived);
        await Expect(page.Locator("#who")).ToHaveTextAsync("who=nobody", Settled);
        Assert.Equal("kept", await page.EvaluateAsync<string>("() => window.raskProbe"));
    }

    private static bool IsIslandAsset(string path) =>
        path.Contains("/_rask/external/", StringComparison.Ordinal)
        || path.Contains("/_content/Rask.External/", StringComparison.Ordinal);
}
