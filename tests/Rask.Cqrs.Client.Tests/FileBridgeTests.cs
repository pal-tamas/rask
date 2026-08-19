using System.Text;
using Rask.Core.Forms;
using Rask.Core.Routing;
using Rask.Cqrs.Client;

namespace Rask.Cqrs.Client.Tests;

/// <summary>
///     The two conversions that keep <c>HttpClient</c> out of a file's round trip: a picked file becomes
///     a message property, and a returned file reaches the user's disk.
/// </summary>
public class FileBridgeTests
{
    [Fact]
    public void AsRemote_carries_the_picked_files_identity()
    {
        var picked = new FakePickedFile("quarterly.csv", "text/csv", "Id,Total\n1,9.99"u8.ToArray(),
            new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero));

        var remote = picked.AsRemote();

        Assert.Equal("quarterly.csv", remote.Name);
        Assert.Equal("text/csv", remote.ContentType);
        Assert.Equal(picked.Size, remote.Size);
        Assert.Equal(picked.LastModified, remote.LastModified);
    }

    [Fact]
    public void AsRemote_does_not_open_the_file_until_the_upload_reads_it()
    {
        // A message built but never dispatched must not have touched the disk: the browser only hands
        // over a file's bytes when they are asked for, and a picked file can be large.
        var picked = new FakePickedFile("big.bin", "application/octet-stream", new byte[64]);

        var remote = picked.AsRemote();
        Assert.Equal(0, picked.Opens);

        using var stream = remote.OpenReadStream();
        Assert.Equal(1, picked.Opens);
    }

    [Fact]
    public void AsRemote_reads_a_file_larger_than_RaskFiles_default_ceiling()
    {
        // RaskFile.OpenReadStream defaults to a 512 KB ceiling. The bridge passes the file's own size
        // instead, so a picked file above that uploads whole rather than being truncated or throwing.
        var payload = new byte[(512 * 1024) + 1];
        Array.Fill(payload, (byte)7);
        var picked = new FakePickedFile("large.bin", "application/octet-stream", payload);

        using var stream = picked.AsRemote().OpenReadStream();
        using var copy = new MemoryStream();
        stream.CopyTo(copy);

        Assert.Equal(payload.Length, copy.Length);
        Assert.Equal(payload.Length, picked.LastRequestedCeiling);
    }

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

    private sealed class FakePickedFile(
        string name,
        string contentType,
        byte[] bytes,
        DateTimeOffset? lastModified = null) : RaskFile
    {
        public int Opens { get; private set; }

        public long LastRequestedCeiling { get; private set; }

        public override string Name => name;

        public override long Size => bytes.Length;

        public override string ContentType => contentType;

        public override DateTimeOffset LastModified => lastModified ?? DateTimeOffset.UnixEpoch;

        public override Stream OpenReadStream(
            long maxAllowedSize = 512 * 1024,
            CancellationToken cancellationToken = default)
        {
            Opens++;
            LastRequestedCeiling = maxAllowedSize;

            if (bytes.Length > maxAllowedSize)
            {
                throw new InvalidOperationException(
                    $"The file is {bytes.Length} bytes, above the {maxAllowedSize} byte ceiling.");
            }

            return new MemoryStream(bytes, writable: false);
        }
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
