using Rask.Core.Routing;

namespace Rask.Core.Tests.Routing;

public class NavigatorDownloadTests
{
    [Fact]
    public void Download_OutsideHandlerScope_Throws()
    {
        var nav = new Navigator(new RouteState(), new RecordingSink());
        Assert.Throws<InvalidOperationException>(() =>
            nav.Download("report.csv", new byte[] { 1, 2, 3 }, "text/csv"));
    }

    [Fact]
    public void Download_InsideHandlerScope_QueuesOnSink_AsBytes()
    {
        var sink = new RecordingSink();
        var nav = new Navigator(new RouteState(), sink);
        using (nav.EnterHandler())
        {
            nav.Download("a.bin", new byte[] { 9, 8, 7 }, "application/octet-stream");
        }

        Assert.Single(sink.Staged);
        var (filename, bytes, _, contentType) = sink.Staged[0];
        Assert.Equal("a.bin", filename);
        Assert.Equal(new byte[] { 9, 8, 7 }, bytes);
        Assert.Equal("application/octet-stream", contentType);
    }

    [Fact]
    public void Download_InsideHandlerScope_QueuesOnSink_AsStream()
    {
        var sink = new RecordingSink();
        var nav = new Navigator(new RouteState(), sink);
        using (nav.EnterHandler())
        {
            nav.Download("b.txt", new MemoryStream(new byte[] { 1, 2 }), "text/plain");
        }

        Assert.Single(sink.Staged);
        var (filename, _, stream, contentType) = sink.Staged[0];
        Assert.Equal("b.txt", filename);
        Assert.NotNull(stream);
        Assert.Equal("text/plain", contentType);
    }

    [Fact]
    public void Download_WithoutSink_Throws()
    {
        var nav = new Navigator(new RouteState());
        using (nav.EnterHandler())
        {
            Assert.Throws<InvalidOperationException>(() => nav.Download("x", new byte[1], "text/plain"));
        }
    }

    private sealed class RecordingSink : IDownloadSink
    {
        public List<(string filename, byte[]? bytes, Stream? stream, string? contentType)> Staged { get; } = new();

        public void Stage(string filename, byte[] bytes, string? contentType)
            => Staged.Add((filename, bytes, null, contentType));

        public void Stage(string filename, Stream stream, string? contentType)
            => Staged.Add((filename, null, stream, contentType));

        public bool TryConsume(out PendingDownload? download)
        {
            download = null;
            return false;
        }
    }
}
