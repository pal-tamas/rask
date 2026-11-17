using System.Buffers.Binary;

namespace Rask.Cqrs;

// What crosses between the page and its worker, one ArrayBuffer each way:
//
//     [int32, little-endian: the header's length][the header, UTF-8 JSON][the blobs, back to back]
//
// The header says how long each blob is. Bytes travel as bytes — a workbook is never turned into a base64 string the
// page has to build and the worker has to take apart — and the JSON stays small enough to read in one pass.
internal static class OffThreadFrame
{
    private const int Prefix = sizeof(int);

    // A frame with room for the blobs after the header, which the caller writes from blobsStart on: the bytes are
    // read straight into it, rather than into an array of their own that would then be copied in.
    internal static byte[] Allocate(ReadOnlySpan<byte> header, long blobs, out int blobsStart)
    {
        var length = checked((int)(Prefix + header.Length + blobs));
        var frame = GC.AllocateUninitializedArray<byte>(length);
        BinaryPrimitives.WriteInt32LittleEndian(frame, header.Length);
        header.CopyTo(frame.AsSpan(Prefix));
        blobsStart = Prefix + header.Length;
        return frame;
    }

    internal static byte[] Compose(ReadOnlySpan<byte> header, ReadOnlySpan<byte> blob)
    {
        var frame = Allocate(header, blob.Length, out var start);
        blob.CopyTo(frame.AsSpan(start));
        return frame;
    }

    internal static (ReadOnlyMemory<byte> Header, int BlobsStart) Split(byte[] frame)
    {
        if (frame.Length < Prefix)
        {
            throw new InvalidDataException("An off-thread frame is shorter than its own length prefix.");
        }

        var header = BinaryPrimitives.ReadInt32LittleEndian(frame);
        if (header < 0 || header > frame.Length - Prefix)
        {
            throw new InvalidDataException("An off-thread frame names a header longer than the frame.");
        }

        return (frame.AsMemory(Prefix, header), Prefix + header);
    }

    // The blobs a header listed, as files over the frame itself: nothing is copied until a codec reads one.
    internal static List<OffThreadBlob> Blobs(byte[] frame, int start, IReadOnlyList<OffThreadBlob.Shape> shapes)
    {
        var blobs = new List<OffThreadBlob>(shapes.Count);
        var offset = start;
        foreach (var shape in shapes)
        {
            if (shape.Length < 0 || shape.Length > frame.Length - offset)
            {
                throw new InvalidDataException("An off-thread frame names a blob longer than the frame.");
            }

            blobs.Add(new OffThreadBlob(shape, frame, offset));
            offset += shape.Length;
        }

        return blobs;
    }
}
