using System.Buffers;
using BenchmarkDotNet.Attributes;
using Rask.Core.Live;
using Rask.Core.Routing;

namespace Rask.Benchmarks;

// The cost a download adds to a render payload: the WASM host ships only a short token (the bytes
// live .NET-side until JS calls PullDownload), so the payload stays O(1) whatever the file size.
[MemoryDiagnoser]
public class DownloadPayloadBenchmarks
{
    private const string Html =
        "<!doctype html><html><head><title>x</title></head><body><div>hello</div></body></html>";

    private static readonly PendingDownload Download =
        new("f.bin", "application/octet-stream", Url: null, Token: "8c7eaa6c6c5d4d97a3b04ea5a3c2f1cd");

    private readonly ArrayBufferWriter<byte> _writer = new(8192);

    [Benchmark]
    public int TokenPull()
    {
        _writer.ResetWrittenCount();
        LivePayload.BuildPayloadUtf8WithRoot(_writer, Html, "wasm", null, false, null, Download);
        return _writer.WrittenCount;
    }
}
