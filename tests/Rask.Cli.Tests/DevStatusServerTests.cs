using System.Net;
using Rask.Cli.Dev;

namespace Rask.Cli.Tests;

/// <summary>
///     The channel that outlives the app: when a rebuild fails there is no app process left to broadcast
///     from, so <c>rask dev</c> answers instead. These exercise the real listener over real HTTP — the
///     value of this piece is entirely in whether a browser can actually reach it.
/// </summary>
public sealed class DevStatusServerTests
{
    [Fact]
    public async Task It_serves_the_watcher_state_over_http()
    {
        var watcher = new DevBuildWatcher();
        using var server = DevStatusServer.TryStart(watcher);
        Assert.NotNull(server);

        using var client = new HttpClient();
        var ok = await client.GetStringAsync(server.Url, TestContext.Current.CancellationToken);

        watcher.Observe("/app/A.cs(1,1): error CS0103: nope [/app/App.csproj]");
        var failed = await client.GetStringAsync(server.Url, TestContext.Current.CancellationToken);

        Assert.Contains("\"state\":\"ok\"", ok, StringComparison.Ordinal);
        // Read live, not snapshotted at start: the whole point is to answer about the build happening now.
        Assert.Contains("\"state\":\"failed\"", failed, StringComparison.Ordinal);
        Assert.Contains("CS0103", failed, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("http://localhost:5000")]
    [InlineData("https://127.0.0.1:7001")]
    [InlineData("https://shop.test")]
    public async Task The_app_s_own_origin_may_read_it_and_it_is_never_cached(string origin)
    {
        // The app is on another port, so every poll is cross-origin; and a cached "failed" would outlive
        // the failure it describes.
        using var server = DevStatusServer.TryStart(new DevBuildWatcher(), ["https://shop.test/", null]);
        Assert.NotNull(server);

        using var client = new HttpClient();
        using var response = await client.SendAsync(Request(HttpMethod.Get, server.Url, origin), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(origin, Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
        Assert.Contains("Origin", string.Join(",", response.Headers.Vary), StringComparison.Ordinal);
        Assert.Contains("no-store", string.Join(",", response.Headers.GetValues("Cache-Control")), StringComparison.Ordinal);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [InlineData("https://evil.example")]
    [InlineData("https://shop.test.evil.example")]
    [InlineData("null")]
    public async Task Any_other_site_is_not_let_read_the_build_s_errors(string origin)
    {
        // The document quotes source paths and code; it was readable from every origin (`*`).
        using var server = DevStatusServer.TryStart(new DevBuildWatcher(), ["https://shop.test"]);
        Assert.NotNull(server);

        using var client = new HttpClient();
        using var response = await client.SendAsync(Request(HttpMethod.Get, server.Url, origin), TestContext.Current.CancellationToken);

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task A_cors_preflight_is_answered()
    {
        using var server = DevStatusServer.TryStart(new DevBuildWatcher());
        Assert.NotNull(server);

        using var client = new HttpClient();
        using var response = await client.SendAsync(Request(HttpMethod.Options, server.Url, "http://localhost:5000"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("http://localhost:5000", Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
    }

    private static HttpRequestMessage Request(HttpMethod method, string url, string origin)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.TryAddWithoutValidation("Origin", origin);
        return request;
    }

    [Fact]
    public void It_binds_loopback_on_an_os_assigned_port()
    {
        // Never a fixed port: a second `rask dev` in another checkout must not fight the first for it.
        using var first = DevStatusServer.TryStart(new DevBuildWatcher());
        using var second = DevStatusServer.TryStart(new DevBuildWatcher());

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotEqual(first.Port, second.Port);
        // `localhost`, not the dotted form: an IP-literal prefix makes HttpListener resolve the
        // machine's own hostname inside Start(), which is a 5s timeout on any box whose hostname is
        // not in /etc/hosts. Pinned here so the fast spelling cannot be "tidied" back.
        Assert.StartsWith("http://localhost:", first.Url, StringComparison.Ordinal);
        Assert.EndsWith("/status", first.Url, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Disposing_releases_the_port()
    {
        var server = DevStatusServer.TryStart(new DevBuildWatcher());
        Assert.NotNull(server);
        var url = server.Url;

        server.Dispose();

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        await Assert.ThrowsAnyAsync<Exception>(() => client.GetStringAsync(url, TestContext.Current.CancellationToken));
    }
}
