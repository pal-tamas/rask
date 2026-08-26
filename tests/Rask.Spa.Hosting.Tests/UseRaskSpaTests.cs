using System.Net;
using System.Net.Http.Headers;
using Rask.Spa.Hosting.Tests.Infrastructure;

namespace Rask.Spa.Hosting.Tests;

public class UseRaskSpaTests
{
    [Fact]
    public async Task A_client_side_route_gets_the_index_document()
    {
        using var dist = new FakeDistDirectory();
        await using var host = await SpaTestServer.CreateAsync(dist.Path);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/orders/42");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
        var response = await host.Http.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("<div id=root>", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_navigation_that_states_no_preference_still_gets_the_index_document()
    {
        using var dist = new FakeDistDirectory();
        await using var host = await SpaTestServer.CreateAsync(dist.Path);

        // No Accept header at all — a bare curl, or an old client. Treated as a navigation, because
        // refusing it would break deep links for anything that does not announce itself.
        var response = await host.Http.GetAsync("/orders/42");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_mapped_api_endpoint_is_not_shadowed_by_the_fallback()
    {
        using var dist = new FakeDistDirectory();
        await using var host = await SpaTestServer.CreateAsync(dist.Path, withApi: true);

        var response = await host.Http.GetAsync("/api/ping");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("pong", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_missing_asset_is_a_404_rather_than_the_index_document()
    {
        using var dist = new FakeDistDirectory();
        await using var host = await SpaTestServer.CreateAsync(dist.Path);

        // The failure this prevents: answering a module import with HTML, which the browser reports
        // as "Failed to load module script" — a message that reads as a broken framework rather than
        // as the missing file it is.
        var response = await host.Http.GetAsync("/assets/does-not-exist.js");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_request_that_does_not_want_html_is_a_404()
    {
        using var dist = new FakeDistDirectory();
        await using var host = await SpaTestServer.CreateAsync(dist.Path);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/not-mapped");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        var response = await host.Http.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task The_index_document_is_never_cached()
    {
        using var dist = new FakeDistDirectory();
        await using var host = await SpaTestServer.CreateAsync(dist.Path);

        var response = await host.Http.GetAsync("/index.html");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-cache", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task A_content_hashed_asset_is_immutable()
    {
        using var dist = new FakeDistDirectory();
        await using var host = await SpaTestServer.CreateAsync(dist.Path);

        var response = await host.Http.GetAsync("/assets/index-DkK9xYz1.js");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("immutable", response.Headers.CacheControl?.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unhashed_root_file_revalidates()
    {
        using var dist = new FakeDistDirectory();
        await using var host = await SpaTestServer.CreateAsync(dist.Path);

        var response = await host.Http.GetAsync("/favicon.svg");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-cache", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task A_precompressed_sibling_keeps_the_real_content_type()
    {
        using var dist = new FakeDistDirectory(withPrecompressed: true);
        await using var host = await SpaTestServer.CreateAsync(dist.Path);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/assets/index-DkK9xYz1.js");
        request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("br"));
        var response = await host.Http.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("br", response.Content.Headers.ContentEncoding);

        // The regression: the middleware rewrites the path to the .br sibling, after which the
        // static-file middleware types the response off that unknown extension and lands on
        // octet-stream. A browser will not execute a script served as octet-stream.
        Assert.Contains(
            "javascript",
            response.Content.Headers.ContentType?.MediaType ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Development_without_a_build_says_where_the_app_actually_is()
    {
        await using var host = await SpaTestServer.CreateAsync(
            distPath: null,
            environment: "Development",
            configure: o => o.DevServerUrl = "http://localhost:5173");

        var response = await host.Http.GetAsync("/");

        // 200, not 503. In development a missing dist/ is the normal state — the bundler is serving
        // the app — so a server error would send people hunting a bug that is not there.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("http://localhost:5173", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Production_without_a_build_is_a_503()
    {
        await using var host = await SpaTestServer.CreateAsync(distPath: null);

        var response = await host.Http.GetAsync("/");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains(
            "single-page app is unavailable",
            await response.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_prefix_scopes_the_app_and_leaves_the_rest_of_the_site_alone()
    {
        using var dist = new FakeDistDirectory();
        await using var host = await SpaTestServer.CreateAsync(dist.Path, pathBase: "/app");

        var asset = await host.Http.GetAsync("/app/assets/index-DkK9xYz1.js");
        Assert.Equal(HttpStatusCode.OK, asset.StatusCode);
        Assert.Contains("immutable", asset.Headers.CacheControl?.ToString(), StringComparison.Ordinal);

        using var deepLink = new HttpRequestMessage(HttpMethod.Get, "/app/orders/42");
        deepLink.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
        Assert.Equal(HttpStatusCode.OK, (await host.Http.SendAsync(deepLink)).StatusCode);

        // Outside the prefix the host answers nothing — which is what lets a second app, or an
        // unrelated set of endpoints, live beside this one.
        using var outside = new HttpRequestMessage(HttpMethod.Get, "/somewhere-else");
        outside.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
        Assert.Equal(HttpStatusCode.NotFound, (await host.Http.SendAsync(outside)).StatusCode);
    }

    [Fact]
    public async Task A_missing_asset_under_a_prefix_is_still_a_404()
    {
        using var dist = new FakeDistDirectory();
        await using var host = await SpaTestServer.CreateAsync(dist.Path, pathBase: "/app");

        // The immutable-prefix rule is written against "/assets/", so it only fires here if the
        // host strips its own prefix before consulting it.
        var response = await host.Http.GetAsync("/app/assets/does-not-exist.js");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task The_published_wwwroot_wins_over_a_build_machine_path()
    {
        using var dist = new FakeDistDirectory();

        // Shaped like a publish output: the app's content root holds a wwwroot with the built app in
        // it. This has to beat any baked path, or a deployed container chases a directory that only
        // ever existed on the build machine.
        var contentRoot = Path.Combine(Path.GetTempPath(), "rask-spa-root-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(contentRoot, "wwwroot"));
        File.WriteAllText(Path.Combine(contentRoot, "wwwroot", "index.html"), "<title>published</title>");

        try
        {
            await using var host = await SpaTestServer.CreateAsync(distPath: null, contentRoot: contentRoot);

            using var request = new HttpRequestMessage(HttpMethod.Get, "/orders/42");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
            var response = await host.Http.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("published", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(contentRoot, recursive: true);
        }
    }
}
