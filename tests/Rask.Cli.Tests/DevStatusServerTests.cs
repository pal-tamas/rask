using System.Net;
using System.Net.Http;
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
        var ok = await client.GetStringAsync(server.Url);

        watcher.Observe("/app/A.cs(1,1): error CS0103: nope [/app/App.csproj]");
        var failed = await client.GetStringAsync(server.Url);

        Assert.Contains("\"state\":\"ok\"", ok, StringComparison.Ordinal);
        // Read live, not snapshotted at start: the whole point is to answer about the build happening now.
        Assert.Contains("\"state\":\"failed\"", failed, StringComparison.Ordinal);
        Assert.Contains("CS0103", failed, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_response_is_cross_origin_readable_and_never_cached()
    {
        // The app is on another port, so every poll is cross-origin; and a cached "failed" would outlive
        // the failure it describes.
        using var server = DevStatusServer.TryStart(new DevBuildWatcher());
        Assert.NotNull(server);

        using var client = new HttpClient();
        using var response = await client.GetAsync(server.Url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("*", Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
        Assert.Contains("no-store", string.Join(",", response.Headers.GetValues("Cache-Control")), StringComparison.Ordinal);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task A_cors_preflight_is_answered()
    {
        using var server = DevStatusServer.TryStart(new DevBuildWatcher());
        Assert.NotNull(server);

        using var client = new HttpClient();
        using var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Options, server.Url));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("*", Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
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
        Assert.StartsWith("http://127.0.0.1:", first.Url, StringComparison.Ordinal);
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
        await Assert.ThrowsAnyAsync<Exception>(() => client.GetStringAsync(url));
    }
}
