using System.Net;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Rask.Meta.Hosting.Tests;

/// <summary>
///     The forwarder, driven end to end over real sockets against a stand-in for the Node server.
/// </summary>
/// <remarks>
///     Real hosts rather than a <c>TestServer</c>, deliberately. The properties worth checking here —
///     that a response is not buffered, that an upgrade is possible at all — are properties of an
///     actual connection, and an in-memory transport would report them as passing whether or not they
///     hold.
/// </remarks>
public class NodeForwarderTests
{
    private static CancellationToken Timeout() =>
        new CancellationTokenSource(TimeSpan.FromSeconds(30)).Token;

    /// <summary>Starts a host on an ephemeral loopback port and reports the port it got.</summary>
    private static async Task<(WebApplication App, int Port)> StartAsync(
        Action<WebApplicationBuilder>? configure,
        Action<WebApplication> map)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        configure?.Invoke(builder);

        var app = builder.Build();
        map(app);
        await app.StartAsync();

        var addresses = app.Services.GetRequiredService<IServer>().Features
            .Get<IServerAddressesFeature>()!;
        return (app, new Uri(addresses.Addresses.First()).Port);
    }

    /// <summary>Starts a Rask host forwarding to <paramref name="nodePort" />.</summary>
    private static Task<(WebApplication App, int Port)> StartRaskAsync(
        int nodePort,
        Action<WebApplication>? map = null) =>
        StartAsync(
            builder => builder.Services.AddRaskMeta(options =>
            {
                options.SuperviseNode = false;
                options.Port = nodePort;
            }),
            app =>
            {
                map?.Invoke(app);
                app.UseRaskMeta();
            });

    private static HttpClient ClientFor(int port) =>
        new() { BaseAddress = new Uri($"http://127.0.0.1:{port}") };

    /// <summary>A request Kestrel does not answer reaches the Node server.</summary>
    [Fact]
    public async Task An_unmatched_request_is_forwarded()
    {
        var (node, nodePort) = await StartAsync(null, app => app.MapGet("/page", () => "from node"));
        await using var _ = node;
        var (rask, raskPort) = await StartRaskAsync(nodePort);
        await using var __ = rask;

        using var client = ClientFor(raskPort);
        Assert.Equal("from node", await client.GetStringAsync("/page", Timeout()));
    }

    /// <summary>
    ///     A mapped endpoint wins over the forwarder.
    /// </summary>
    /// <remarks>
    ///     The ordering contract in one assertion. What is registered is a fallback, so the API answers
    ///     its own routes and only what is left over crosses to Node — and the failure this guards is
    ///     the memorable one: an API call answered with a rendered HTML page.
    /// </remarks>
    [Fact]
    public async Task A_mapped_endpoint_wins_over_the_forwarder()
    {
        var (node, nodePort) = await StartAsync(null, app => app.MapFallback(() => "from node"));
        await using var _ = node;
        var (rask, raskPort) = await StartRaskAsync(
            nodePort, app => app.MapGet("/_rask/ping", () => "from rask"));
        await using var __ = rask;

        using var client = ClientFor(raskPort);
        Assert.Equal("from rask", await client.GetStringAsync("/_rask/ping", Timeout()));
        Assert.Equal("from node", await client.GetStringAsync("/anything-else", Timeout()));
    }

    /// <summary>
    ///     The response is streamed through rather than buffered.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The single most important property in this package, and the one that fails silently:
    ///         React Server Components and streaming SSR send a page over many flushes, and a proxy
    ///         that waits for the last byte turns a streaming page into a slow blank one while every
    ///         other test still passes.
    ///     </para>
    ///     <para>
    ///         The backend deliberately will not finish until the client has already read the first
    ///         chunk, so buffering cannot merely look slow here — it deadlocks, and the timeout turns
    ///         that into a failure instead of a hang.
    ///     </para>
    /// </remarks>
    [Fact]
    public async Task The_response_is_not_buffered()
    {
        var released = new TaskCompletionSource();

        var (node, nodePort) = await StartAsync(null, app => app.MapGet("/stream", async (HttpContext context) =>
        {
            context.Response.ContentType = "text/plain";
            await context.Response.WriteAsync("first");
            await context.Response.Body.FlushAsync();
            await released.Task;
            await context.Response.WriteAsync("second");
        }));
        await using var _ = node;
        var (rask, raskPort) = await StartRaskAsync(nodePort);
        await using var __ = rask;

        using var client = ClientFor(raskPort);
        using var response = await client.GetAsync(
            "/stream", HttpCompletionOption.ResponseHeadersRead, Timeout());
        await using var stream = await response.Content.ReadAsStreamAsync(Timeout());

        var head = new byte[5];
        await stream.ReadExactlyAsync(head, Timeout());
        Assert.Equal("first", Encoding.UTF8.GetString(head));

        // Only now does the backend get to finish, so reaching this line at all proves the first
        // chunk crossed before the response completed.
        released.SetResult();

        using var reader = new StreamReader(stream);
        Assert.Equal("second", await reader.ReadToEndAsync(Timeout()));
    }

    /// <summary>
    ///     While the Node process is not listening, requests are refused rather than forwarded.
    /// </summary>
    /// <remarks>
    ///     503 with <c>Retry-After</c> rather than the 502 that forwarding into a closed port produces:
    ///     for the first seconds of a container's life this state is normal and temporary, and it
    ///     should say so in the terms clients and orchestrators already act on.
    /// </remarks>
    [Fact]
    public async Task A_request_before_node_is_listening_is_refused_with_503()
    {
        var (node, nodePort) = await StartAsync(null, app => app.MapFallback(() => "from node"));
        await using var _ = node;
        var (rask, raskPort) = await StartRaskAsync(nodePort);
        await using var __ = rask;

        rask.Services.GetRequiredService<NodeReadiness>().MarkNotReady();

        using var client = ClientFor(raskPort);
        using var response = await client.GetAsync("/page", Timeout());

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("1", response.Headers.RetryAfter?.Delta?.TotalSeconds.ToString("0"));
    }

    /// <summary>Forwarding without the services that start the process is a startup error.</summary>
    [Fact]
    public async Task UseRaskMeta_without_AddRaskMeta_throws()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        await using var app = builder.Build();

        var error = Assert.Throws<InvalidOperationException>(() => app.UseRaskMeta());
        Assert.Contains("AddRaskMeta", error.Message, StringComparison.Ordinal);
    }
}
