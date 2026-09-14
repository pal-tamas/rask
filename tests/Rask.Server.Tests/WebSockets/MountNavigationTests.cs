using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Core.Routing;
using Rask.Server.Tests.Infrastructure;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated chain entries
#pragma warning disable RASK019 // test-infra apps predate framework-managed <head>

namespace Rask.Server.Tests.WebSockets;

// #1094: a session belongs to the application on this host it was opened for. The GET already resolved against
// that application's table; a live navigation used to resolve against every assembly's, so a host-app session
// could render a mounted application's pages inside its own document, and the reverse. A navigation to a path
// another application owns now becomes a real page load, so that application's GET builds its own root.
[Collection(Name)]
public sealed class MountNavigationTests : IDisposable
{
    public const string Name = "RouteRegistryMutation";

    // Two group keys standing in for two assemblies' generated route registries. The registry recovers an
    // application's provenance from the key's assembly, so a type from this assembly is the host and a type
    // from another assembly (CoreLib) is the mount.
    private static readonly object _hostKey = typeof(MountNavigationTests);
    private static readonly object _mountKey = typeof(string);

    public MountNavigationTests()
    {
        RouteRegistry.Replace(_hostKey, [
            new RouteRegistration(typeof(HostPage), "/host-page", null),
            new RouteRegistration(typeof(OtherHostPage), "/host-other", null),
        ]);
        RouteRegistry.Replace(_mountKey, [new RouteRegistration(typeof(MountedPage), "/_mounted", null)]);
    }

    public void Dispose()
    {
        RouteRegistry.Replace(_hostKey, []);
        RouteRegistry.Replace(_mountKey, []);
    }

    [Fact]
    public async Task AHostSession_NavigatingToAMountedPath_IsSentToLoadItAsAPage()
    {
        using var host = CreateHost();
        using var ws = await ConnectAsync(host, "/host-page");

        await ws.SendJsonAsync(new { type = "navigate", path = "/_mounted", query = "?tab=1" });

        var frame = await ws.TryReceiveTextAsync(TimeSpan.FromSeconds(5));
        Assert.NotNull(frame);
        Assert.DoesNotContain("mounted-page", frame);
        AssertLocation(frame, "/_mounted?tab=1", replace: false);
    }

    [Fact]
    public async Task AMountedSession_NavigatingToAHostPath_IsSentToLoadItAsAPage()
    {
        using var host = CreateHost();
        using var ws = await ConnectAsync(host, "/_mounted");

        await ws.SendJsonAsync(new { type = "navigate", path = "/host-page", query = "", replace = true });

        var frame = await ws.TryReceiveTextAsync(TimeSpan.FromSeconds(5));
        Assert.NotNull(frame);
        Assert.DoesNotContain("host-page-body", frame);
        AssertLocation(frame, "/host-page", replace: true);
    }

    // The scoping must not cost a session its own application's pages.
    [Fact]
    public async Task AHostSession_NavigatingWithinTheHost_RendersInPlace()
    {
        using var host = CreateHost();
        using var ws = await ConnectAsync(host, "/host-page");

        await ws.SendJsonAsync(new { type = "navigate", path = "/host-other", query = "" });

        var frame = await ws.TryReceiveTextAsync(TimeSpan.FromSeconds(5));
        Assert.NotNull(frame);
        Assert.Contains("host-other-body", frame);
    }

    // The Router a host session renders defaults to the host's table too, so a path that only the mount declares
    // matches nothing there even when the route state reaches it without a navigate frame.
    [Fact]
    public async Task AHostSessionsRouter_DoesNotMatchAMountedPage()
    {
        using var host = CreateHost();
        var html = await host.Http.GetStringAsync("/host-page");
        var session = host.Store.Get(MarkupAssert.SessionId(html))!;

        var table = session.Services.GetRequiredService<RouteState>().CurrentTable;

        Assert.DoesNotContain(RouteFlattener.Flatten(table), leaf => leaf.Chain[^1] == typeof(MountedPage));
        Assert.Contains(RouteFlattener.Flatten(table), leaf => leaf.Chain[^1] == typeof(HostPage));
    }

    private static RaskTestHost CreateHost() =>
        RaskTestHost.Create<HostRoot>(services =>
            services.AddSingleton(new RaskMountedApp(typeof(MountedRoot), "/_mounted/{**path}", typeof(string).Assembly)));

    private static async Task<System.Net.WebSockets.WebSocket> ConnectAsync(RaskTestHost host, string path)
    {
        var html = await host.Http.GetStringAsync(path);
        var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await ws.SendJsonAsync(new { type = "hello", session = MarkupAssert.SessionId(html) });
        _ = await ws.TryReceiveTextAsync(TimeSpan.FromMilliseconds(300));
        return ws;
    }

    private static void AssertLocation(string frame, string url, bool replace)
    {
        using var doc = JsonDocument.Parse(frame);
        Assert.Equal("location", doc.RootElement.GetProperty("type").GetString());
        Assert.Equal(url, doc.RootElement.GetProperty("url").GetString());
        Assert.Equal(replace, doc.RootElement.GetProperty("replace").GetBoolean());
    }

    private sealed class HostRoot : Component
    {
        protected override Component? Render() => Div.Id("host-root")[Router];
    }

    private sealed class MountedRoot : Component
    {
        protected override Component? Render() => Div.Id("mounted-root")[Router];
    }

    private sealed class HostPage : Component
    {
        protected override Component? Render() => P["host-page-body"];
    }

    private sealed class OtherHostPage : Component
    {
        protected override Component? Render() => P["host-other-body"];
    }

    private sealed class MountedPage : Component
    {
        protected override Component? Render() => P["mounted-page"];
    }
}

[CollectionDefinition(MountNavigationTests.Name, DisableParallelization = true)]
public sealed class RouteRegistryMutationCollection;
