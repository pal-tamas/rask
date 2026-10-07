using System.Net;
using Microsoft.AspNetCore.Builder;
using Rask.Core.ScopedAssets;
using Rask.Server.Tests.Infrastructure;
using Rask.Spa.Hosting;

namespace Rask.Server.Tests.Endpoints;

/// <summary>
///     An app that already has an API and a single-page front end takes Rask pages under a prefix: the
///     three live in one process and each answers only its own paths.
/// </summary>
/// <remarks>
///     This is how an existing app moves onto Rask a page at a time — <c>MapRask&lt;TApp&gt;(pathBase:)</c>
///     for the pages that have moved, <c>MapRaskSpa()</c> for everything the old front end still owns.
/// </remarks>
[Collection("ScopedAssets")]
public sealed class ExistingSpaBesideRaskTests : IDisposable
{
    private const string SpaIndex = "<!doctype html><title>old spa</title>";

    private readonly string _dist = Directory.CreateTempSubdirectory("rask-existing-spa-").FullName;

    public ExistingSpaBesideRaskTests()
    {
        ScopedAssetRegistry.InvalidateAll();
        File.WriteAllText(Path.Combine(_dist, "index.html"), SpaIndex);
        Directory.CreateDirectory(Path.Combine(_dist, "assets"));
        File.WriteAllText(Path.Combine(_dist, "assets", "app.3f9a1c2b.js"), "console.log('old spa')");
    }

    public void Dispose() => Directory.Delete(_dist, recursive: true);

    [Fact]
    public async Task A_page_under_the_prefix_is_rendered_by_Rask()
    {
        using var host = CreateHost();

        var body = await Get(host, "/new/");

        Assert.Contains("path=/", body, StringComparison.Ordinal);
        Assert.DoesNotContain("old spa", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_route_the_old_front_end_owns_gets_its_index_document()
    {
        using var host = CreateHost();

        var body = await Get(host, "/vehicles/42");

        Assert.Equal(SpaIndex, body);
    }

    [Fact]
    public async Task The_api_answers_its_own_paths()
    {
        using var host = CreateHost();

        var body = await Get(host, "/api/version");

        Assert.Equal("7", body);
    }

    [Fact]
    public async Task The_old_front_ends_hashed_file_is_served_from_the_root()
    {
        using var host = CreateHost();

        var response = await host.Http.GetAsync("/assets/app.3f9a1c2b.js", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/javascript", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task The_Rask_runtime_is_served_under_the_prefix_only()
    {
        using var host = CreateHost();

        var prefixed = await host.Http.GetAsync("/new/rask/rask.js", TestContext.Current.CancellationToken);
        var atRoot = await host.Http.GetAsync("/rask/rask.js", TestContext.Current.CancellationToken);

        Assert.Equal("text/javascript", prefixed.Content.Headers.ContentType?.MediaType);
        Assert.NotEqual("text/javascript", atRoot.Content.Headers.ContentType?.MediaType);
    }

    private RaskTestHost CreateHost() =>
        RaskTestHost.Create<TestApp>(
            pathBase: "/new",
            configureMiddleware: app =>
            {
                var endpoints = (WebApplication)app;
                endpoints.MapGet("/api/version", () => "7");
                endpoints.MapRaskSpa(_dist);
            });

    private static async Task<string> Get(RaskTestHost host, string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Accept.ParseAdd("text/html");
        var response = await host.Http.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
    }
}
