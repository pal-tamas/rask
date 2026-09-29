using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Server.Tests.App;

/// <summary>
///     <see cref="RaskApp.Serve"/> — the server of a WebAssembly client — driven over real HTTP, like
///     <see cref="RaskAppTests"/> and for the same reason: what it owns is the ORDER, and the browser app's fallback
///     answering a path that belonged to something else looks correct in any list of registrations.
/// </summary>
/// <remarks>
///     No client is built under a test runner, so <c>MapRaskSpa</c> answers every path it owns with its
///     missing-bundle page: a 503 naming the single-page app. That answer is the evidence a path reached the
///     browser app's fallback rather than the server.
/// </remarks>
[Collection(RaskAppCollection.Name)]
public sealed class RaskAppServeTests
{
    private const string SpaUnavailable = "single-page app is unavailable";

    private static WebApplication NewServedApp(Action<RaskAppOptions>? configure = null)
    {
        var app = RaskApp.Create([], b => b.WebHost.UseSetting("urls", "http://127.0.0.1:0"));
        if (configure is not null)
        {
            app.Configure(configure);
        }

        return app.BuildServe();
    }

    private static async Task<(HttpStatusCode Status, string Body)> GetAsync(WebApplication app, string path)
    {
        var address = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses.First();

        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            BaseAddress = new Uri(address),
        };
        using var response = await client.GetAsync(path);
        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    private static async Task<(HttpStatusCode Status, string Body)> AskAsync(WebApplication app, string path)
    {
        await app.StartAsync();
        try
        {
            return await GetAsync(app, path);
        }
        finally
        {
            await app.StopAsync();
        }
    }

    [Fact]
    public async Task A_path_nothing_else_claims_falls_through_to_the_browser_app()
    {
        var app = NewServedApp();

        var (status, body) = await AskAsync(app, "/orders/42");

        // Run<App> would render a page here with a 200. Serve has no root of its own: the browser app's fallback
        // answers, and with no bundle built it says so.
        Assert.Equal(HttpStatusCode.ServiceUnavailable, status);
        Assert.Contains(SpaUnavailable, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_operator_console_keeps_its_prefix_ahead_of_the_browser_apps_fallback()
    {
        var app = NewServedApp();

        var (status, body) = await AskAsync(app, "/_rask");

        // Mounted, so the server answers — whatever the console decides about an anonymous visitor, it is not the
        // browser app's page.
        Assert.NotEqual(HttpStatusCode.ServiceUnavailable, status);
        Assert.DoesNotContain(SpaUnavailable, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Turning_the_console_off_hands_its_prefix_to_the_browser_app()
    {
        var app = NewServedApp(c => c.Ops.Off());

        var (status, body) = await AskAsync(app, "/_rask");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, status);
        Assert.Contains(SpaUnavailable, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_browser_apps_messages_reach_the_dispatch_endpoint()
    {
        var app = NewServedApp();

        var (status, body) = await AskAsync(app, "/_rask/cqrs/request/NoSuchMessage");

        // Remote dispatch answers an unknown name itself — refusing it rather than rendering anything.
        Assert.NotEqual(HttpStatusCode.ServiceUnavailable, status);
        Assert.NotEqual(HttpStatusCode.OK, status);
        Assert.DoesNotContain(SpaUnavailable, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_the_mediator_there_is_no_dispatch_endpoint()
    {
        var app = NewServedApp(c => c.Cqrs.Off());

        var (status, body) = await AskAsync(app, "/_rask/cqrs/request/NoSuchMessage");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, status);
        Assert.Contains(SpaUnavailable, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_service_worker_is_left_to_the_browser_app()
    {
        var app = NewServedApp();

        var (status, _) = await AskAsync(app, "/rask-sw.js");

        // Client/'s bundle ships its own worker. A server route for this path would answer before the bundle's
        // file, so Serve maps none, and with no bundle the path is simply missing.
        Assert.Equal(HttpStatusCode.NotFound, status);
    }

    [Fact]
    public async Task The_health_endpoint_answers()
    {
        var app = NewServedApp();

        var (status, _) = await AskAsync(app, "/health");

        Assert.Equal(HttpStatusCode.OK, status);
    }
}
