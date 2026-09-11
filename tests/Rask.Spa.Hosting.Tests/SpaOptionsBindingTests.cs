using System.Net;
using Rask.Spa.Hosting.Tests.Infrastructure;

namespace Rask.Spa.Hosting.Tests;

// SpaHostingOptions are read from Rask:Spa while UseRaskSpa maps the app, and the callback passed to it is applied on
// top. Nothing lives in DI, so the document the fallback serves is the thing to observe: the fake bundle carries both
// its usual index.html and an app.html, and each test checks which one a client route gets.
public class SpaOptionsBindingTests
{
    private const string AppDocument = "<!doctype html><title>app.html</title><div id=configured></div>";

    [Fact]
    public async Task The_Rask_Spa_section_is_read_while_the_app_is_mapped()
    {
        using var dist = DistWithAppDocument();
        await using var host = await SpaTestServer.CreateAsync(
            dist.Path,
            settings: new() { ["Rask:Spa:IndexFileName"] = "app.html" });

        var body = await GetClientRouteAsync(host);

        Assert.Contains("<div id=configured>", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_callback_wins_over_the_section()
    {
        using var dist = DistWithAppDocument();
        await using var host = await SpaTestServer.CreateAsync(
            dist.Path,
            configure: o => o.IndexFileName = "index.html",
            settings: new() { ["Rask:Spa:IndexFileName"] = "app.html" });

        var body = await GetClientRouteAsync(host);

        Assert.Contains("<div id=root>", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_top_level_Spa_section_is_not_read()
    {
        using var dist = DistWithAppDocument();
        await using var host = await SpaTestServer.CreateAsync(
            dist.Path,
            settings: new() { ["Spa:IndexFileName"] = "app.html" });

        var body = await GetClientRouteAsync(host);

        Assert.Contains("<div id=root>", body, StringComparison.Ordinal);
    }

    private static FakeDistDirectory DistWithAppDocument()
    {
        var dist = new FakeDistDirectory();
        File.WriteAllText(Path.Combine(dist.Path, "app.html"), AppDocument);
        return dist;
    }

    private static async Task<string> GetClientRouteAsync(SpaTestServer host)
    {
        var response = await host.Http.GetAsync("/orders/42");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }
}
