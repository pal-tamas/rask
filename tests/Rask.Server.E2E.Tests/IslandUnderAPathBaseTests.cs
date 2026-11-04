using System.Collections.Concurrent;
using Rask.Core;
using Rask.Core.Routing;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Server.E2E.Tests.Islands;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // small test pages; their <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     An island on a Rask.Server app deployed under a sub-path, in a real browser: the runtime script, the manifest and
///     the chunk are all asked for under the path base, and the island's own front-end code navigates with the app's
///     typed routes through the Server host's bridge, without a reload.
/// </summary>
/// <remarks>
///     The C# half writes the script's URL under the base and the client half reads the base back off that URL
///     (<c>ExternalPathBaseTests</c> pins both in isolation). Only a browser loading a real bundle from a real host shows
///     the two meeting: a chunk asked for at the site root is a 404 here, and the island never mounts.
/// </remarks>
// The path base is one value for the whole process, so the suites that map an app under one take turns.
[Collection(PathBaseCollection.Name)]
public sealed class IslandUnderAPathBaseTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    private const string PathBase = "/shop";

    [Fact]
    public async Task An_island_under_a_path_base_loads_its_script_manifest_and_chunk_under_the_base()
    {
        var requested = new ConcurrentQueue<string>();

        await using var session = await HookSession.OpenAsync<IslandApp>(
            playwright, path: PathBase + "/isle", pathBase: PathBase, islands: true,
            beforeLoad: page => Record(page, requested));

        await Expect(session.Page.GetByTestId("island-caption")).ToHaveTextAsync("mounted under the base");
        var paths = requested.Select(url => new Uri(url).AbsolutePath).ToList();
        Assert.Contains(PathBase + "/_content/Rask.External/rask-external.js", paths);
        Assert.Contains(PathBase + "/_rask/external/manifest.json", paths);
        Assert.Contains(paths, path => path.StartsWith(PathBase + "/_rask/external/", StringComparison.Ordinal)
                                       && path.EndsWith(".js", StringComparison.Ordinal));
        Assert.DoesNotContain(paths, path => path.StartsWith("/_rask/", StringComparison.Ordinal)
                                             || path.StartsWith("/_content/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Front_end_code_navigates_with_the_apps_typed_routes_under_the_path_base_without_a_reload()
    {
        await using var session = await HookSession.OpenAsync<IslandApp>(
            playwright, path: PathBase + "/isle", pathBase: PathBase, islands: true);
        var page = session.Page;
        await Expect(page.GetByTestId("island-go")).ToBeVisibleAsync();
        await page.EvaluateAsync("() => { window.raskProbe = 'kept'; }");

        await page.GetByTestId("island-go").ClickAsync();

        await Expect(page.Locator("#arrived")).ToHaveTextAsync("arrived");
        Assert.Equal(session.BaseUrl + PathBase + "/isle/arrived", page.Url);
        Assert.Equal("kept", await page.EvaluateAsync<string>("() => window.raskProbe"));
    }

    // Every request the page makes, from its first: the runtime script is asked for while the document parses.
    private static Task Record(Microsoft.Playwright.IPage page, ConcurrentQueue<string> requested)
    {
        page.Request += (_, request) => requested.Enqueue(request.Url);
        return Task.CompletedTask;
    }
}

public sealed partial class IslandApp : Component
{
    protected override Component? Render() => Router;
}

[Route("/isle")]
public sealed partial class IslandStartPage : Component
{
    protected override Component? Render() =>
    [
        H1["An island under a path base"],
        IslandGo.Caption("mounted under the base"),
    ];
}

[Route("/isle/arrived")]
public sealed partial class IslandArrivedPage : Component
{
    protected override Component? Render() => P.Id("arrived")["arrived"];
}
