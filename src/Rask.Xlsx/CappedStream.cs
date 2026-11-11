using System.Globalization;

namespace Rask.Workbooks;

/// <summary>A part of the package as it unpacks, refused once it passes its cap - whatever size the archive claimed for it.</summary>
internal sealed class CappedStream(Stream inner, long cap, string part) : Stream
{
    private long read;

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => read;
        set => throw new NotSupportedException();
    }

    public static InvalidDataException TooLarge(string part, long cap) => new(string.Create(
        CultureInfo.InvariantCulture,
        $"The part '{part}' of the workbook unpacks to more than {cap:N0} bytes. Xlsx.Read stops there, so a file that unpacks without end cannot use up the memory."));

    public override int Read(byte[] buffer, int offset, int count) => Counted(inner.Read(buffer, offset, count));

    public override int Read(Span<byte> buffer) => Counted(inner.Read(buffer));

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        Counted(await inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false));

    public override void Flush()
    {
        // nothing is written
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            inner.Dispose();
        }

        base.Dispose(disposing);
    }

    private int Counted(int count)
    {
        read += count;
        return read > cap ? throw TooLarge(part, cap) : count;
    }
}
