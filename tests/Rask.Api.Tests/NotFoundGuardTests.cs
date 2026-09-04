using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Rask.Api.Tests;

/// <summary>
///     The guard that keeps a wrong URL under the API prefix from being answered with a web page.
/// </summary>
/// <remarks>
///     Every test here stands up a real server with a stand-in for Rask's catch-all — a plain
///     <c>MapGet("/{**path}")</c> returning HTML, which is exactly what <c>UseRask</c> registers. Nothing
///     is asserted by reading the endpoint table: the whole point is which endpoint a request reaches,
///     and route precedence is the sort of thing that reads correct in a list and answers wrong over
///     HTTP. That mistake is what this feature exists to correct, so this suite does not repeat it.
/// </remarks>
public sealed class NotFoundGuardTests
{
    private const string AppMarker = "<!DOCTYPE html><p>the app</p>";

    private static async Task<IHost> StartAsync(
        Action<IEndpointRouteBuilder>? map = null,
        Action<ApiOptions>? configure = null)
    {
        var host = await new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddRaskApi(configure);
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        map?.Invoke(endpoints);
                        endpoints.MapRaskApi();

                        // Stands in for UseRask's catch-all: an ordinary MapGet at the default order,
                        // serving the app for anything unmatched.
                        endpoints.MapGet("/{**path}", () => Results.Content(AppMarker, "text/html"));
                    });
                }))
            .StartAsync();

        return host;
    }

    [Fact]
    public async Task An_unmatched_api_path_answers_404_as_a_problem_document()
    {
        // The defect this guard exists for. Without it the catch-all renders the app, so a typo answers
        // 200 with HTML and the caller's JSON parse fails a long way from the cause.
        using var host = await StartAsync();

        var response = await host.GetTestClient().GetAsync("/api/typo");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"status\":404", body, StringComparison.Ordinal);
        Assert.Contains("/api/typo", body, StringComparison.Ordinal);
        Assert.DoesNotContain("DOCTYPE", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_real_api_route_still_wins_over_the_guard()
    {
        // The guard is a catch-all of its own, so the thing to prove is that it yields to everything
        // underneath it. If it did not, it would be a far worse bug than the one it fixes.
        using var host = await StartAsync(e => e.MapGet("/api/items/{id}", (int id) => Results.Json(new { id })));

        var response = await host.GetTestClient().GetAsync("/api/items/7");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("{\"id\":7}", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_wrong_verb_on_an_api_path_answers_404_rather_than_405()
    {
        // Rask's catch-all is a MapGet, so a POST to a wrong /api path would otherwise reach nothing at
        // all and 405 -- a status meaning "this route exists, not like that", which is the wrong thing
        // to tell someone whose URL is simply mistyped. The guard names every verb for that reason.
        using var host = await StartAsync();

        var response = await host.GetTestClient().PostAsync("/api/typo", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task A_path_outside_the_prefix_still_reaches_the_app()
    {
        // The other half: the guard must not swallow page routes. If it did, every page in the app
        // would 404 -- so this is the test that would fail if the pattern were widened by accident.
        using var host = await StartAsync();

        var response = await host.GetTestClient().GetAsync("/some/deep/page");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(AppMarker, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task The_prefix_is_configurable()
    {
        using var host = await StartAsync(configure: o => o.Prefix = "/services");

        var guarded = await host.GetTestClient().GetAsync("/services/typo");
        var unguarded = await host.GetTestClient().GetAsync("/api/typo");

        Assert.Equal(HttpStatusCode.NotFound, guarded.StatusCode);
        Assert.Equal(HttpStatusCode.OK, unguarded.StatusCode);
        Assert.Equal(AppMarker, await unguarded.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task The_guard_can_be_turned_off()
    {
        using var host = await StartAsync(configure: o => o.NotFound = false);

        var response = await host.GetTestClient().GetAsync("/api/typo");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(AppMarker, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Mapping_the_api_after_the_catch_all_works_just_the_same()
    {
        // Registration order is not what makes any of this work -- precedence is. This pins that for the
        // guard specifically, because the guard is the one endpoint here whose whole job is to beat
        // another catch-all, and "it happened to be registered first" would be a false reason for it to
        // pass. See RaskAppTests.An_endpoint_mapped_after_UseRask_still_runs for the same point about
        // an app's own endpoints.
        using var host = await new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddRaskApi();
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGet("/{**path}", () => Results.Content(AppMarker, "text/html"));
                        endpoints.MapRaskApi();
                    });
                }))
            .StartAsync();

        var response = await host.GetTestClient().GetAsync("/api/typo");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public void An_invalid_prefix_is_refused_where_it_is_set()
    {
        // Not at map time, where the stack trace points at framework code rather than at the line that
        // wrote it.
        var options = new ApiOptions();

        Assert.Throws<ArgumentException>(() => options.Prefix = "api");
        Assert.Throws<ArgumentException>(() => options.Prefix = " ");
    }

    [Fact]
    public void A_trailing_slash_on_the_prefix_does_not_change_what_it_matches()
    {
        // "/api/" and "/api" name the same thing to a reader, and a pattern built by concatenation would
        // turn the first into "/api//{**rest}", which matches nothing.
        var options = new ApiOptions { Prefix = "/api/" };

        Assert.Equal("/api", options.Prefix);
    }

    [Fact]
    public void Mapping_without_registering_says_which_call_is_missing()
    {
        var services = new ServiceCollection();
        services.AddRouting();
        using var provider = services.BuildServiceProvider();

        var error = Assert.Throws<InvalidOperationException>(
            () => new TestEndpointRouteBuilder(provider).MapRaskApi());

        Assert.Contains("AddRaskApi()", error.Message, StringComparison.Ordinal);
    }

    private sealed class TestEndpointRouteBuilder(IServiceProvider serviceProvider) : IEndpointRouteBuilder
    {
        public IServiceProvider ServiceProvider { get; } = serviceProvider;

        public ICollection<EndpointDataSource> DataSources { get; } = [];

        public IApplicationBuilder CreateApplicationBuilder() =>
            new ApplicationBuilder(ServiceProvider);
    }
}
