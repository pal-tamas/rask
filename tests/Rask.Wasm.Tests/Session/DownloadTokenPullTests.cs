using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Core.Routing;
using Rask.Wasm.Files;

#pragma warning disable RASK019 // test-infra apps predate framework-managed <head>

namespace Rask.Wasm.Tests.Session;

[Collection("WasmSession")]
public class DownloadTokenPullTests : ResettingTestBase
{
    [Fact]
    public async Task DownloadTriggeredFromHandler_PayloadCarriesTokenNotBase64Bytes()
    {
        var bytes = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        var (session, _) = NewSessionWithDownloadOnClick("manifest.bin", bytes, "application/octet-stream");

        var initial = await session.InitialRenderAsync();
        var handlerId = MarkupAssert.FirstHandlerId(initial);

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
        var handlerId = MarkupAssert.FirstHandlerId(initial);
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
        string filename, byte[] bytes, string? contentType) =>
        NewSession<DownloadStubApp>(
            p => new DownloadStubApp(p.GetRequiredService<Navigator>(), filename, bytes, contentType),
            s => s.AddSingleton<IDownloadSink, WasmDownloadSink>());

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

        protected override Component? HeadAssets => Title["dl"];
        protected override string? HtmlLang => null;

        protected override Component? Render() => Button.OnClick(() => _nav.Download(_filename, _bytes, _contentType))["go"];
    }
}
