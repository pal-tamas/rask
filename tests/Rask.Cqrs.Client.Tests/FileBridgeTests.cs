using System.Text;
using Rask.Core.Routing;
using Rask.Cqrs.Client;

namespace Rask.Cqrs.Client.Tests;

/// <summary>
///     The download half of a file's round trip. There is no upload half to test: a message declares its
///     file as a <c>RaskFile</c>, so the file a user picked is passed to the handler unconverted.
/// </summary>
public class FileBridgeTests
{
    [Fact]
    public void Download_hands_the_returned_file_to_the_hosts_sink()
    {
        var sink = new RecordingSink();
        var navigator = new Navigator(new RouteState(), sink);
        var download = FileDownload.FromBytes("report.csv", "text/csv", "Id,Total\n1,9.99"u8.ToArray());

        using (navigator.EnterHandler())
        {
            navigator.Download(download);
        }

        var staged = Assert.Single(sink.Staged);
        Assert.Equal("report.csv", staged.Filename);
        Assert.Equal("text/csv", staged.ContentType);
        Assert.NotNull(staged.Stream);
        Assert.Equal("Id,Total\n1,9.99", new StreamReader(staged.Stream!, Encoding.UTF8).ReadToEnd());
    }

    [Fact]
    public void Download_streams_rather_than_buffering_the_file()
    {
        // The stream is handed over, not read: an export larger than memory must not land in memory on
        // its way to the sink. If this ever starts buffering, the sink receives bytes instead.
        var sink = new RecordingSink();
        var navigator = new Navigator(new RouteState(), sink);
        var download = FileDownload.FromStream("export.bin", "application/octet-stream",
            new MemoryStream(new byte[] { 1, 2, 3 }));

        using (navigator.EnterHandler())
        {
            navigator.Download(download);
        }

        var staged = Assert.Single(sink.Staged);
        Assert.NotNull(staged.Stream);
        Assert.Null(staged.Bytes);
    }

    [Fact]
    public void Download_outside_an_event_handler_throws()
    {
        // Inherited from Navigator deliberately: a browser only starts a save in response to something
        // the user did, so a download staged during a render would be silently dropped.
        var navigator = new Navigator(new RouteState(), new RecordingSink());
        var download = FileDownload.FromBytes("report.csv", "text/csv", [1]);

        Assert.Throws<InvalidOperationException>(() => navigator.Download(download));
    }

    private sealed record Staged(string Filename, byte[]? Bytes, Stream? Stream, string? ContentType);

    private sealed class RecordingSink : IDownloadSink
    {
        public List<Staged> Staged { get; } = [];

        public void Stage(string filename, byte[] bytes, string? contentType) =>
            Staged.Add(new Staged(filename, bytes, null, contentType));

        public void Stage(string filename, Stream stream, string? contentType) =>
            Staged.Add(new Staged(filename, null, stream, contentType));

        public bool TryConsume(out PendingDownload? download)
        {
            download = null;
            return false;
        }
    }
}
