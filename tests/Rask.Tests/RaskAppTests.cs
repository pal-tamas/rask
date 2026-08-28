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
        // The seam that exists because the bug is invisible. UseRask ends the pipeline with a catch-all
        // that serves the app for anything unmatched, so an endpoint mapped after it never runs — and
        // does not error either: the request renders the app where the author expected JSON.
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
