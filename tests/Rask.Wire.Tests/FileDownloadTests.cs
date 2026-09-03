namespace Rask.Wire.Tests;

public sealed class FileDownloadTests
{
    [Fact]
    public void FromBytes_carries_its_length_so_the_response_can_report_progress()
    {
        var download = FileDownload.FromBytes("report.csv", "text/csv", "a,b\n1,2"u8.ToArray());

        Assert.Equal("report.csv", download.FileName);
        Assert.Equal("text/csv", download.ContentType);
        Assert.Equal(7, download.Length);
    }

    [Fact]
    public void A_missing_content_type_defaults_rather_than_travelling_empty()
    {
        Assert.Equal("application/octet-stream", FileDownload.FromBytes("a", null, []).ContentType);
        Assert.Equal("application/octet-stream", FileDownload.FromBytes("a", "", []).ContentType);
    }

    [Fact]
    public void A_seekable_stream_contributes_its_remaining_length()
    {
        var stream = new MemoryStream("abcdef"u8.ToArray());
        stream.Position = 2;

        Assert.Equal(4, FileDownload.FromStream("a", null, stream).Length);
    }

    [Fact]
    public void A_non_seekable_stream_sends_chunked_rather_than_guessing_a_length()
    {
        Assert.Null(FileDownload.FromStream("a", null, new NonSeekableStream("abc"u8.ToArray())).Length);
    }

    [Fact]
    public void An_explicit_length_wins_over_the_stream_s_own()
    {
        var stream = new MemoryStream("abcdef"u8.ToArray());

        Assert.Equal(2, FileDownload.FromStream("a", null, stream, 2).Length);
    }

    [Fact]
    public async Task WriteToAsync_copies_the_content_and_disposes_the_source()
    {
        var source = new NonSeekableStream("payload"u8.ToArray());
        var download = FileDownload.FromStream("a", null, source);
        using var destination = new MemoryStream();

        await download.WriteToAsync(destination);

        Assert.Equal("payload"u8.ToArray(), destination.ToArray());
        Assert.True(source.Disposed);
    }

    [Fact]
    public async Task WriteToAsync_does_not_dispose_the_destination_so_the_response_stays_writable()
    {
        var destination = new NonSeekableStream([]);

        await FileDownload.FromBytes("a", null, "x"u8.ToArray()).WriteToAsync(destination);

        Assert.False(destination.Disposed);
    }

    [Fact]
    public void Reading_the_content_twice_throws_instead_of_yielding_nothing()
    {
        var download = FileDownload.FromBytes("a", null, "x"u8.ToArray());

        download.OpenReadStream().Dispose();

        var second = Assert.Throws<InvalidOperationException>(() => download.OpenReadStream());
        Assert.Contains("already been read", second.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteToAsync_after_a_read_throws_for_the_same_reason()
    {
        var download = FileDownload.FromBytes("a", null, "x"u8.ToArray());
        download.OpenReadStream().Dispose();

        using var destination = new MemoryStream();
        await Assert.ThrowsAsync<InvalidOperationException>(() => download.WriteToAsync(destination));
    }

    [Fact]
    public void A_download_without_a_name_or_content_is_rejected_at_construction()
    {
        Assert.Throws<ArgumentException>(() => FileDownload.FromBytes("", null, []));
        Assert.Throws<ArgumentNullException>(() => FileDownload.FromBytes("a", null, null!));
        Assert.Throws<ArgumentNullException>(() => FileDownload.FromStream("a", null, null!));
    }

    // Stands in for a network body or a pipe: the shapes a FileDownload actually wraps in production,
    // where Length and Position both throw and disposal is the thing worth observing.
    private sealed class NonSeekableStream : Stream
    {
        private readonly MemoryStream _inner;

        // Expandable, and seeded by writing rather than by the byte[] constructor: that overload produces
        // a fixed-capacity stream, so the double could be read but never written to.
        public NonSeekableStream(byte[] content)
        {
            _inner = new MemoryStream();
            _inner.Write(content, 0, content.Length);
            _inner.Position = 0;
        }

        public bool Disposed { get; private set; }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => _inner.Flush();

        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => _inner.Write(buffer, offset, count);

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }
}
