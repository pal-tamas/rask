using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Live;
using Rask.Native.Files;
using Rask.Native.Tests.Infrastructure;

namespace Rask.Native.Tests.Session;

// Navigator.Download end to end on the native host: a click stages bytes, the frame carries a token, the
// client hands the token back, and the file reaches the platform. Before IDownloadSink was registered here
// the first step threw — a shared component that downloaded on Server and WASM crashed on native.
[Collection("NativeSession")]
public sealed class NativeDownloadTests() : ResettingTestBase(LiveDiffMode.DisabledFull)
{
    [Fact]
    public async Task Download_FromAHandler_ShipsATokenInTheFrame()
    {
        NativeDownloadApp.FileName = "report.txt";
        var (_, webView, initial) = await NativeSessionHarness.NewSessionAsync<NativeDownloadApp>(
            diffMode: DiffMode);
        var handlerId = MarkupAssert.FirstHandlerId(initial);

        await webView.PostAsync($$"""{"id":"{{handlerId}}","type":"click"}""");

        using var doc = JsonDocument.Parse(webView.LastFrame.AsMemory());
        var download = doc.RootElement.GetProperty("download");
        Assert.Equal("report.txt", download.GetProperty("filename").GetString());
        Assert.Equal("text/plain", download.GetProperty("contentType").GetString());
        // Token-pull, like the WASM host: the bytes stay .NET-side rather than riding the frame as base64.
        Assert.NotEmpty(download.GetProperty("token").GetString()!);
        Assert.False(download.TryGetProperty("bytes", out _));
    }

    [Fact]
    public async Task ClientReturningTheToken_HandsTheFileToThePlatform()
    {
        NativeDownloadApp.FileName = "report.txt";
        var export = new FakeFileExport();
        var (_, webView, initial) = await NativeSessionHarness.NewSessionAsync<NativeDownloadApp>(
            configure: s => s.AddSingleton<INativeFileExport>(export), diffMode: DiffMode);

        await webView.PostAsync($$"""{"id":"{{MarkupAssert.FirstHandlerId(initial)}}","type":"click"}""");
        await webView.PostAsync($$"""{"type":"download","token":"{{TokenFrom(webView.LastFrame)}}"}""");

        var file = Assert.Single(export.Exported);
        Assert.Equal("report.txt", file.FileName);
        Assert.Equal("text/plain", file.ContentType);
        Assert.Equal("hello native", await File.ReadAllTextAsync(file.Path));
    }

    [Fact]
    public async Task ATokenReplayedBySomeMisbehavingClient_ExportsOnce()
    {
        NativeDownloadApp.FileName = "report.txt";
        var export = new FakeFileExport();
        var (_, webView, initial) = await NativeSessionHarness.NewSessionAsync<NativeDownloadApp>(
            configure: s => s.AddSingleton<INativeFileExport>(export), diffMode: DiffMode);

        await webView.PostAsync($$"""{"id":"{{MarkupAssert.FirstHandlerId(initial)}}","type":"click"}""");
        var token = TokenFrom(webView.LastFrame);
        await webView.PostAsync($$"""{"type":"download","token":"{{token}}"}""");
        await webView.PostAsync($$"""{"type":"download","token":"{{token}}"}""");

        Assert.Single(export.Exported);
    }

    // A download name can be attacker-influenced (a record title, a filename echoed back from an API), and on
    // this host it becomes a real path. The traversal has to be cut before the join, not after.
    [Fact]
    public async Task ATraversingFileName_StagesInsideTheDownloadDirectory()
    {
        NativeDownloadApp.FileName = "../../../../etc/passwd";
        var export = new FakeFileExport();
        var (_, webView, initial) = await NativeSessionHarness.NewSessionAsync<NativeDownloadApp>(
            configure: s => s.AddSingleton<INativeFileExport>(export), diffMode: DiffMode);

        await webView.PostAsync($$"""{"id":"{{MarkupAssert.FirstHandlerId(initial)}}","type":"click"}""");
        await webView.PostAsync($$"""{"type":"download","token":"{{TokenFrom(webView.LastFrame)}}"}""");

        var file = Assert.Single(export.Exported);
        Assert.Equal("passwd", file.FileName);
        Assert.StartsWith(
            Path.Combine(Path.GetTempPath(), "rask-downloads"), file.Path, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnUnknownToken_IsIgnoredRatherThanFaultingTheMessagePump()
    {
        var export = new FakeFileExport();
        var (_, webView, _) = await NativeSessionHarness.NewSessionAsync<NativeDownloadApp>(
            configure: s => s.AddSingleton<INativeFileExport>(export), diffMode: DiffMode);

        await webView.PostAsync("""{"type":"download","token":"deadbeef"}""");

        Assert.Empty(export.Exported);
    }

    private static string TokenFrom(byte[] frame)
    {
        using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(frame));
        return doc.RootElement.GetProperty("download").GetProperty("token").GetString()!;
    }

    private sealed class FakeFileExport : INativeFileExport
    {
        public List<NativeFileExport> Exported { get; } = [];

        public ValueTask ExportAsync(NativeFileExport file)
        {
            Exported.Add(file);
            return default;
        }
    }
}
