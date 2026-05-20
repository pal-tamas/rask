using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Core.Routing;
using Rask.Core.ScopedCss;
using Rask.Wasm.Files;
using static Rask.Core.Components.Components;

#pragma warning disable RASK019 // test-infra apps predate framework-managed <head>

namespace Rask.Wasm.Tests.Session;

[Collection("WasmSession")]
public class DownloadTokenPullTests
{
    public DownloadTokenPullTests() => ScopedCssRegistry.InvalidateAll();

    [Fact]
    public async Task DownloadTriggeredFromHandler_PayloadCarriesTokenNotBase64Bytes()
    {
        var bytes = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        var (session, _) = NewSessionWithDownloadOnClick("manifest.bin", bytes, "application/octet-stream");

        var initial = await session.InitialRenderAsync();
        var handlerId = ExtractFirstHandlerId(initial);

        var payload =
            await session.DispatchAsync(Encoding.UTF8.GetBytes($$"""{"id":"{{handlerId}}","type":"click"}"""));

        using var doc = JsonDocument.Parse(payload.AsMemory());
        var download = doc.RootElement.GetProperty("download");
        Assert.Equal("manifest.bin", download.GetProperty("filename").GetString());
        Assert.Equal("application/octet-stream", download.GetProperty("contentType").GetString());
        Assert.True(download.TryGetProperty("token", out var tokenProp), "expected token in payload");
        Assert.False(string.IsNullOrEmpty(tokenProp.GetString()));
        Assert.False(download.TryGetProperty("bytes", out _), "bytes must not be inlined when token transport is used");
    }

    [Fact]
    public async Task PullDownload_AfterDispatch_ReturnsBytesAndDrainsToken()
    {
        var bytes = new byte[] { 9, 8, 7, 6, 5, 4 };
        var (session, _) = NewSessionWithDownloadOnClick("a.bin", bytes, null);

        var initial = await session.InitialRenderAsync();
        var handlerId = ExtractFirstHandlerId(initial);
        var payload =
            await session.DispatchAsync(Encoding.UTF8.GetBytes($$"""{"id":"{{handlerId}}","type":"click"}"""));
        var token = ExtractToken(payload);

        var pulled = JSInterop.PullDownload(token);
        Assert.Equal(bytes, pulled);

        // Second pull drains: returns empty, idempotent under double-click.
        var second = JSInterop.PullDownload(token);
        Assert.Empty(second);
    }

    [Fact]
    public void PullDownload_UnknownToken_ReturnsEmpty()
    {
        var (_, _) = NewSessionWithDownloadOnClick("noop.bin", new byte[] { 1 }, null);
        Assert.Empty(JSInterop.PullDownload("no-such-token"));
        Assert.Empty(JSInterop.PullDownload(""));
    }

    private static (WasmLiveSession session, IServiceProvider services) NewSessionWithDownloadOnClick(
        string filename, byte[] bytes, string? contentType)
    {
        var services = new ServiceCollection();
        services.AddSingleton<RouteState>();
        services.AddSingleton<Navigator>();
        services.AddSingleton<IDownloadSink, WasmDownloadSink>();
        var provider = services.BuildServiceProvider();
        var app = new DownloadStubApp(provider.GetRequiredService<Navigator>(), filename, bytes, contentType);
        var session = new WasmLiveSession(app, provider);
        JSInterop.Init(session);
        return (session, provider);
    }

    private static string ExtractFirstHandlerId(byte[] payload)
    {
        using var doc = JsonDocument.Parse(payload.AsMemory());
        var html = doc.RootElement.GetProperty("html").GetString()!;
        var match = Regex.Match(html, "data-rask-on-click=\"(h\\d+)\"");
        Assert.True(match.Success);
        return match.Groups[1].Value;
    }

    private static string ExtractToken(byte[] payload)
    {
        using var doc = JsonDocument.Parse(payload.AsMemory());
        return doc.RootElement.GetProperty("download").GetProperty("token").GetString()!;
    }

    private sealed class DownloadStubApp : Component
    {
        private readonly byte[] _bytes;
        private readonly string? _contentType;
        private readonly string _filename;
        private readonly Navigator _nav;

        public DownloadStubApp(Navigator nav, string filename, byte[] bytes, string? contentType)
        {
            _nav = nav;
            _filename = filename;
            _bytes = bytes;
            _contentType = contentType;
        }

        protected override Component Render() =>
            Fragment()[
                Doctype(),
                Html()[
                    Head()[Title()["dl"]],
                    Body()[
                        Button(OnClick: () => _nav.Download(_filename, _bytes, _contentType))["go"]
                    ]
                ]];
    }
}
