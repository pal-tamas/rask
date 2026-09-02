using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Globalization;


namespace Rask.Tests;

/// <summary>
///     <see cref="RaskApp"/> drives a real host over real HTTP.
/// </summary>
/// <remarks>
///     Deliberately not asserted by inspecting the service collection or the endpoint table. The whole
///     value of this type is the pipeline ORDER, and every ordering bug it exists to prevent — an endpoint
///     mapped after the catch-all, a health probe that 307s because it sits behind the HTTPS redirect —
///     looks perfectly correct in a list of registrations. They only show up when a request goes through.
///     So each test starts the app on a real port and asks it a question.
/// </remarks>

public sealed class RaskAppTests
{
    // Port 0 lets the OS choose, and the bound address is read back off the server feature. A fixed port
    // would collide with the other suites on this machine, which is a documented source of red runs here.
    private static RaskApp NewApp(Action<RaskApp>? arrange = null)
    {
        var app = RaskApp.Create([], b => b.WebHost.UseSetting("urls", "http://127.0.0.1:0"));
        arrange?.Invoke(app);
        return app;
    }

    private static string BaseAddress(WebApplication app) =>
        app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses.First();

    private static async Task<HttpResponseMessage> GetAsync(WebApplication app, string path)
    {
        using var client = new HttpClient { BaseAddress = new Uri(BaseAddress(app)) };
        return await client.GetAsync(path);
    }

    [Fact]
    public async Task An_app_with_no_configuration_at_all_serves_its_root()
    {
        // The headline: this is the entire Program.cs of a working Rask app.
        var app = NewApp().Build<TestApp>();
        await app.StartAsync();

        try
        {
            var response = await GetAsync(app, "/");
            Assert.True(response.IsSuccessStatusCode, $"root answered {(int)response.StatusCode}");
        }
        finally
        {
            await app.StopAsync();
        }
    }

    [Fact]
    public async Task An_app_with_no_configuration_serves_the_service_worker()
    {
        // The seam, not the parts. AddRask() registers IWebPush/INotifications/IBadge/IWakeLock
        // unconditionally, but on a Server host their JS helper is served only by AddRaskPwa -- which
        // nothing called before the Pwa battery existed. All four injected fine and then failed on a 404,
        // and a test asserting the REGISTRATIONS would have passed throughout. This one asks the running
        // server for the file.
        var app = NewApp().Build<TestApp>();
        await app.StartAsync();

        try
        {
            var response = await GetAsync(app, "/rask-sw.js");
            Assert.True(
                response.IsSuccessStatusCode,
                $"the service worker answered {(int)response.StatusCode}; "
                + "IWebPush.RegisterServiceWorkerAsync() defaults to this URL, so a failure here is a "
                + "runtime failure for every push subscription");

            // Status alone would prove nothing: UseRask ends in a catch-all serving the app for any
            // unmatched path, so an ABSENT worker answers 200 with HTML. The content type is the evidence.
            Assert.Equal("text/javascript", response.Content.Headers.ContentType?.MediaType);
            Assert.Contains("push", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await app.StopAsync();
        }
    }

    [Fact]
    public async Task An_app_with_no_configuration_is_installable()
    {
        // Name is `required` on WebAppManifest, so the default cannot be empty -- it is the app's own
        // name, which makes a freshly created app installable without configuring anything.
        var app = NewApp().Build<TestApp>();
        await app.StartAsync();

        try
        {
            var response = await GetAsync(app, "/rask/manifest.webmanifest");
            Assert.True(response.IsSuccessStatusCode, $"the manifest answered {(int)response.StatusCode}");

            var manifest = await response.Content.ReadAsStringAsync();
            Assert.Contains("\"name\"", manifest, StringComparison.Ordinal);
        }
        finally
        {
            await app.StopAsync();
        }
    }

    [Fact]
    public async Task Turning_the_pwa_battery_off_stops_serving_the_worker()
    {
        // Off means off: the battery is on by default, so the only evidence that `c.Pwa.Off()` does
        // anything is that the file stops being served.
        var app = NewApp(a => a.Configure(c => c.Pwa.Off())).Build<TestApp>();
        await app.StartAsync();

        try
        {
            // Not a 404: the catch-all still answers this path with the app itself. What must be gone is
            // the JavaScript — which is why the previous test checks the content type rather than the code.
            var response = await GetAsync(app, "/rask-sw.js");
            Assert.NotEqual("text/javascript", response.Content.Headers.ContentType?.MediaType);
        }
        finally
        {
            await app.StopAsync();
        }
    }

    [Fact]
    public async Task The_health_endpoint_answers_over_plain_http()
    {
        // It has to short-circuit BEFORE UseHttpsRedirection. `rask deploy` probes it internally over
        // plain HTTP with no X-Forwarded-Proto, so a redirected endpoint 307s to a port nothing listens
        // on and the blue-green swap is gated on a probe that can never succeed.
        var app = NewApp().Build<TestApp>();
        await app.StartAsync();

        try
        {
            var response = await GetAsync(app, "/health");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        finally
        {
            await app.StopAsync();
        }
    }

    [Fact]
    public async Task An_endpoint_mapped_through_MapEndpoints_is_reachable()
    {
        // The seam works. What it is NOT is a fix for an ordering bug — see the test below, which maps
        // the same endpoint on the other side of the catch-all and gets the same answer.
        var app = NewApp(a => a.MapEndpoints(e => e.MapGet("/ping", () => "pong"))).Build<TestApp>();
        await app.StartAsync();

        try
        {
            var response = await GetAsync(app, "/ping");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("pong", await response.Content.ReadAsStringAsync());
        }
        finally
        {
            await app.StopAsync();
        }
    }

    [Fact]
    public async Task An_endpoint_mapped_after_UseRask_still_runs()
    {
        // This repo told itself for a long time that an endpoint mapped after UseRask "never runs — and
        // does not error either: the request renders the app where the author expected JSON", and said so
        // in RaskApp.MapEndpoints' own docs, in Rask.Spa.Hosting, in the scaffolded Program.cs and in the
        // comment that used to sit on the test above. Nothing pinned it, and it is not true.
        //
        // Rask's catch-all is a plain MapGet("/{**path}") — an ordinary endpoint, not a terminal
        // middleware and not MapFallback. Endpoint routing matches on PRECEDENCE, never on registration
        // order, and every route an app writes is more specific than a catch-all. So this test maps three
        // endpoints AFTER Build<TApp>() has already run UseRask, and all three answer.
        //
        // It exists to keep the false version from coming back into the docs.
        var app = NewApp().Build<TestApp>();

        app.MapGet("/ping", () => "pong");
        app.MapGet("/api/items/{id}", (int id) => Results.Json(new { id }));
        app.MapPost("/api/items", () => Results.Json(new { created = true }));

        await app.StartAsync();

        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(BaseAddress(app)) };

            var literal = await client.GetAsync("/ping");
            Assert.Equal(HttpStatusCode.OK, literal.StatusCode);
            Assert.Equal("pong", await literal.Content.ReadAsStringAsync());

            var parameterised = await client.GetAsync("/api/items/7");
            Assert.Equal("{\"id\":7}", await parameterised.Content.ReadAsStringAsync());

            // The catch-all is MapGet, so a POST proves the verb is not what saves this either.
            var posted = await client.PostAsync("/api/items", content: null);
            Assert.Equal("{\"created\":true}", await posted.Content.ReadAsStringAsync());
        }
        finally
        {
            await app.StopAsync();
        }
    }

    [Fact]
    public async Task An_unmatched_route_still_reaches_the_app_rather_than_the_mapped_endpoints()
    {
        // The other half of the ordering: user endpoints go first, but they must not swallow the
        // catch-all. If they did, every page in the app would 404.
        var app = NewApp(a => a.MapEndpoints(e => e.MapGet("/ping", () => "pong"))).Build<TestApp>();
        await app.StartAsync();

        try
        {
            var response = await GetAsync(app, "/some/deep/page");
            Assert.True(response.IsSuccessStatusCode, $"deep route answered {(int)response.StatusCode}");
        }
        finally
        {
            await app.StopAsync();
        }
    }

    [Fact]
    public async Task Forwarded_headers_are_not_trusted_unless_the_app_says_so()
    {
        // BehindProxy is the one host default that stays opt-in: trusting these headers from an arbitrary
        // client lets it forge its own IP, and only the deployment knows whether a proxy is really there.
        // Off, the header must not reach Request.Scheme — asserted through the app rather than by reading
        // options back, because what matters is whether the middleware ran.
        string? scheme = null;
        var app = NewApp(a => a.MapEndpoints(e => e.MapGet("/scheme", (HttpContext ctx) =>
        {
            scheme = ctx.Request.Scheme;
            return ctx.Request.Scheme;
        }))).Build<TestApp>();

        await app.StartAsync();

        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(BaseAddress(app)) };
            client.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");
            await client.GetAsync("/scheme");

            Assert.Equal("http", scheme);
        }
        finally
        {
            await app.StopAsync();
        }
    }

    [Fact]
    public async Task Behind_a_proxy_the_forwarded_scheme_is_honoured()
    {
        string? scheme = null;
        var app = NewApp(a =>
        {
            a.Configure(c => c.BehindProxy = true);
            a.MapEndpoints(e => e.MapGet("/scheme", (HttpContext ctx) =>
            {
                scheme = ctx.Request.Scheme;
                return ctx.Request.Scheme;
            }));
        }).Build<TestApp>();

        await app.StartAsync();

        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(BaseAddress(app)) };
            client.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");
            await client.GetAsync("/scheme");

            Assert.Equal("https", scheme);
        }
        finally
        {
            await app.StopAsync();
        }
    }

    [Fact]
    public void Cultures_named_in_Configure_actually_reach_the_app()
    {
        // AddRask's options go in with TryAddSingleton, so the FIRST call wins and any later one is
        // silently discarded — which is exactly what RASK056 reports. Create must therefore NOT call
        // AddRask: if it did, the culture list would be frozen empty before Configure ran, and the app
        // would ship with no languages while its Program.cs plainly listed two.
        var app = NewApp(a => a.Configure(c =>
        {
            c.Cultures.Add("en");
            c.Cultures.Add("hu");
        })).Build<TestApp>();

        var cultures = app.Services.GetRequiredService<RaskCultureOptions>().SupportedCultures;

        Assert.Equal(["en", "hu"], cultures);
    }

    [Fact]
    public void An_app_that_names_no_culture_leaves_localization_off()
    {
        var app = NewApp().Build<TestApp>();

        Assert.Empty(app.Services.GetRequiredService<RaskCultureOptions>().SupportedCultures);
    }

    [Fact]
    public void The_host_defaults_still_apply_through_the_facade()
    {
        // RaskApp.Create calls AddRask, so everything Phase A moved into the framework comes with it.
        var app = NewApp().Build<TestApp>();

        var options = app.Services
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<Microsoft.Extensions.Hosting.HostOptions>>()
            .Value;

        Assert.True(options.ServicesStopConcurrently);
        Assert.True(options.ShutdownTimeout < TimeSpan.FromSeconds(20));
    }
}
