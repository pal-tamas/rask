using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Rask.Meta.Hosting.Tests;

/// <summary>
///     Built client assets are served by Kestrel; everything else still reaches Node.
/// </summary>
/// <remarks>
///     The dividing line is <b>a file on disk</b>, not the shape of the URL. That matters because a
///     meta framework serves plenty of dotted paths dynamically — a generated <c>/sitemap.xml</c>, an
///     API route ending in <c>.json</c> — and any rule based on "looks like a file" would 404 exactly
///     those. It is the same mistake as the <c>{*path:nonfile}</c> fallback, one layer up.
/// </remarks>
[Collection(MetaHostCollection.Name)]
public class StaticAssetsTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "rask-meta-static-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
            // Never created.
        }
    }

    private static CancellationToken Timeout() =>
        new CancellationTokenSource(TimeSpan.FromSeconds(30)).Token;

    private void Write(string relative, string content)
    {
        var path = Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

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

    private Task<(WebApplication App, int Port)> StartRaskAsync(int nodePort, MetaFramework framework) =>
        StartAsync(
            builder => builder.Services.AddRaskMeta(options =>
            {
                options.SuperviseNode = false;
                options.Port = nodePort;
                options.Framework = framework;
                options.AppDirectory = _root;
            }),
            app => app.UseRaskMeta());

    private static HttpClient ClientFor(int port) =>
        new() { BaseAddress = new Uri($"http://127.0.0.1:{port}") };

    /// <summary>Next's two roots are served, from the two different places they live.</summary>
    [Fact]
    public async Task Next_assets_are_served_by_kestrel()
    {
        Write(".next/static/chunks/main.abc123.js", "console.log(1)");
        Write("public/robots.txt", "User-agent: *");

        var (node, nodePort) = await StartAsync(
            null, app => app.MapFallback("{*path}", () => "from node"));
        await using var _ = node;
        var (rask, raskPort) = await StartRaskAsync(nodePort, MetaFramework.Next);
        await using var __ = rask;

        using var client = ClientFor(raskPort);

        Assert.Equal(
            "console.log(1)",
            await client.GetStringAsync("/_next/static/chunks/main.abc123.js", Timeout()));
        Assert.Equal("User-agent: *", await client.GetStringAsync("/robots.txt", Timeout()));
    }

    /// <summary>Nitro's four keep their assets in one place, and it is served the same way.</summary>
    [Fact]
    public async Task Nitro_assets_are_served_by_kestrel()
    {
        Write(".output/public/_nuxt/entry.abc.js", "export default 1");

        var (node, nodePort) = await StartAsync(
            null, app => app.MapFallback("{*path}", () => "from node"));
        await using var _ = node;
        var (rask, raskPort) = await StartRaskAsync(nodePort, MetaFramework.Nuxt);
        await using var __ = rask;

        using var client = ClientFor(raskPort);
        Assert.Equal("export default 1", await client.GetStringAsync("/_nuxt/entry.abc.js", Timeout()));
    }

    /// <summary>
    ///     A dotted path with no file behind it still forwards.
    /// </summary>
    /// <remarks>
    ///     The guard that keeps a generated <c>/sitemap.xml</c> working. Serving static assets must not
    ///     reintroduce, by the back door, the "anything with a dot is a file" rule that broke asset
    ///     forwarding in the first place.
    /// </remarks>
    [Fact]
    public async Task A_generated_dotted_path_still_reaches_node()
    {
        Write("public/robots.txt", "User-agent: *");

        var (node, nodePort) = await StartAsync(
            null, app => app.MapFallback("{*path}", () => "generated by node"));
        await using var _ = node;
        var (rask, raskPort) = await StartRaskAsync(nodePort, MetaFramework.Next);
        await using var __ = rask;

        using var client = ClientFor(raskPort);
        Assert.Equal("generated by node", await client.GetStringAsync("/sitemap.xml", Timeout()));
    }

    /// <summary>
    ///     Content-hashed assets are immutable; everything else revalidates.
    /// </summary>
    /// <remarks>
    ///     Only the prefixes there is evidence for are marked immutable. Being wrong in the cautious
    ///     direction costs a revalidation; being wrong the other way strands a visitor on a stale chunk
    ///     until they clear their browser.
    /// </remarks>
    [Fact]
    public async Task Hashed_assets_are_immutable_and_the_rest_are_not()
    {
        Write(".next/static/chunks/main.abc123.js", "console.log(1)");
        Write("public/robots.txt", "User-agent: *");

        var (node, nodePort) = await StartAsync(
            null, app => app.MapFallback("{*path}", () => "from node"));
        await using var _ = node;
        var (rask, raskPort) = await StartRaskAsync(nodePort, MetaFramework.Next);
        await using var __ = rask;

        using var client = ClientFor(raskPort);

        using var hashed = await client.GetAsync("/_next/static/chunks/main.abc123.js", Timeout());
        using var plain = await client.GetAsync("/robots.txt", Timeout());

        Assert.Equal(
            "public, max-age=31536000, immutable",
            hashed.Headers.CacheControl?.ToString());
        Assert.Equal("no-cache", plain.Headers.CacheControl?.ToString());
    }

    /// <summary>A JavaScript chunk is served as JavaScript, not as bytes.</summary>
    /// <remarks>
    ///     A module served as <c>application/octet-stream</c> is refused by the browser with
    ///     "Failed to load module script", which reads as a broken framework rather than a bad header.
    /// </remarks>
    [Fact]
    public async Task An_asset_carries_its_real_content_type()
    {
        Write(".next/static/chunks/main.abc123.js", "console.log(1)");

        var (node, nodePort) = await StartAsync(
            null, app => app.MapFallback("{*path}", () => "from node"));
        await using var _ = node;
        var (rask, raskPort) = await StartRaskAsync(nodePort, MetaFramework.Next);
        await using var __ = rask;

        using var client = ClientFor(raskPort);
        using var response = await client.GetAsync("/_next/static/chunks/main.abc123.js", Timeout());

        Assert.Equal("text/javascript", response.Content.Headers.ContentType?.MediaType);
    }
}
