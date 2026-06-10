using System.Buffers;
using BenchmarkDotNet.Attributes;
using Rask.Core.Live;
using Rask.Core.Routing;

namespace Rask.Benchmarks;

// Compares the legacy base64-inline download path (bytes serialised into the JSON render
// payload via WriteBase64String) against the new token-pull path (only a short token in
// the payload; bytes live .NET-side until JS calls PullDownload). Demonstrates both the
// payload byte-size delta and the build-time allocation delta — the token path scales
// with O(1) regardless of download size, the base64 path scales with O(N).
[MemoryDiagnoser]
public class DownloadPayloadBenchmarks
{
    private const string Html =
        "<!doctype html><html><head><title>x</title></head><body><div>hello</div></body></html>";

    private readonly ArrayBufferWriter<byte> _writer = new(8192);
    private byte[] _largeBytes = null!;
    private byte[] _mediumBytes = null!;
    private byte[] _smallBytes = null!;

    [GlobalSetup]
    public void Setup()
    {
        _smallBytes = new byte[64 * 1024]; // 64 KB
        _mediumBytes = new byte[512 * 1024]; // 512 KB
        _largeBytes = new byte[4 * 1024 * 1024]; // 4 MB
        new Random(42).NextBytes(_smallBytes);
        new Random(43).NextBytes(_mediumBytes);
        new Random(44).NextBytes(_largeBytes);
    }

    [Benchmark]
    [BenchmarkCategory("64KB")]
    public int Base64Inline_64KB() => BuildBase64Payload(_smallBytes);

    [Benchmark]
    [BenchmarkCategory("64KB")]
    public int TokenPull_64KB() => BuildTokenPayload();

    [Benchmark]
    [BenchmarkCategory("512KB")]
    public int Base64Inline_512KB() => BuildBase64Payload(_mediumBytes);

    [Benchmark]
    [BenchmarkCategory("512KB")]
    public int TokenPull_512KB() => BuildTokenPayload();

    [Benchmark]
    [BenchmarkCategory("4MB")]
    public int Base64Inline_4MB() => BuildBase64Payload(_largeBytes);

    [Benchmark]
    [BenchmarkCategory("4MB")]
    public int TokenPull_4MB() => BuildTokenPayload();

    private int BuildBase64Payload(byte[] bytes)
    {
        _writer.ResetWrittenCount();
        var dl = new PendingDownload("f.bin", "application/octet-stream", null, bytes);
        LivePayload.BuildPayloadUtf8WithRoot(_writer, Html, "wasm", null, false, null, dl);
        return _writer.WrittenCount;
    }

    private int BuildTokenPayload()
    {
        _writer.ResetWrittenCount();
        // The token is short and constant — bytes never enter the payload, they live in the
        // download sink keyed by this token until JS pulls them.
        var dl = new PendingDownload("f.bin", "application/octet-stream", null, null,
            "8c7eaa6c6c5d4d97a3b04ea5a3c2f1cd");
        LivePayload.BuildPayloadUtf8WithRoot(_writer, Html, "wasm", null, false, null, dl);
        return _writer.WrittenCount;
    }
}
