using System.Buffers.Binary;
using System.Text;
using System.Text.Unicode;

namespace Rask.Storage.Upload;

/// <summary>
/// The media type of a file, read from its first bytes. The browser's claimed type is never consulted.
/// </summary>
/// <remarks>
/// <para>
/// Signatures first, then markup (the WHATWG MIME-sniffing patterns, so anything a browser would render as
/// HTML or SVG is named as such here), then UTF-8 text, then <c>application/octet-stream</c>. What a type
/// means for serving is <see cref="ContentTypePolicy"/>'s decision, not this one's — a polyglot that starts
/// with a GIF header is <c>image/gif</c> here and is safe there because an image cannot run script.
/// </para>
/// </remarks>
internal static class ContentSniffer
{
    /// <summary>How many bytes the sniffer looks at.</summary>
    internal const int HeadLength = 1024;

    internal const string OctetStream = "application/octet-stream";

    private static ReadOnlySpan<byte> Png => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static ReadOnlySpan<byte> Jpeg => [0xFF, 0xD8, 0xFF];
    private static ReadOnlySpan<byte> Ebml => [0x1A, 0x45, 0xDF, 0xA3];
    private static ReadOnlySpan<byte> ZipLocal => [0x50, 0x4B, 0x03, 0x04];
    private static ReadOnlySpan<byte> ZipEmpty => [0x50, 0x4B, 0x05, 0x06];
    private static ReadOnlySpan<byte> Gzip => [0x1F, 0x8B];
    private static ReadOnlySpan<byte> SevenZip => [0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C];
    private static ReadOnlySpan<byte> Rar => [0x52, 0x61, 0x72, 0x21, 0x1A, 0x07];
    private static ReadOnlySpan<byte> TiffLittle => [0x49, 0x49, 0x2A, 0x00];
    private static ReadOnlySpan<byte> TiffBig => [0x4D, 0x4D, 0x00, 0x2A];
    private static ReadOnlySpan<byte> Ico => [0x00, 0x00, 0x01, 0x00];
    private static ReadOnlySpan<byte> Elf => [0x7F, 0x45, 0x4C, 0x46];
    private static ReadOnlySpan<byte> Utf8Bom => [0xEF, 0xBB, 0xBF];
    private static ReadOnlySpan<byte> Utf16Big => [0xFE, 0xFF];
    private static ReadOnlySpan<byte> Utf16Little => [0xFF, 0xFE];

    // WHATWG "identifying a resource with an unknown MIME type": each must be followed by a tag-terminating byte.
    private static readonly byte[][] HtmlTags =
    [
        "<!DOCTYPE HTML"u8.ToArray(), "<HTML"u8.ToArray(), "<HEAD"u8.ToArray(), "<SCRIPT"u8.ToArray(),
        "<IFRAME"u8.ToArray(), "<H1"u8.ToArray(), "<DIV"u8.ToArray(), "<FONT"u8.ToArray(), "<TABLE"u8.ToArray(),
        "<A"u8.ToArray(), "<STYLE"u8.ToArray(), "<TITLE"u8.ToArray(), "<B"u8.ToArray(), "<BODY"u8.ToArray(),
        "<BR"u8.ToArray(), "<P"u8.ToArray(), "<IMG"u8.ToArray(), "<SVG"u8.ToArray(), "<OBJECT"u8.ToArray(),
        "<EMBED"u8.ToArray(), "<FORM"u8.ToArray(), "<META"u8.ToArray(), "<LINK"u8.ToArray(),
    ];

    internal static string Sniff(ReadOnlySpan<byte> head)
    {
        if (head.IsEmpty)
        {
            return OctetStream;
        }

        return Signature(head) ?? Markup(head) ?? (IsText(head) ? "text/plain" : OctetStream);
    }

    private static string? Signature(ReadOnlySpan<byte> h)
    {
        if (h.StartsWith(Png))
        {
            return "image/png";
        }

        if (h.StartsWith(Jpeg))
        {
            return "image/jpeg";
        }

        if (h.StartsWith("GIF87a"u8) || h.StartsWith("GIF89a"u8))
        {
            return "image/gif";
        }

        if (h.Length >= 12 && h.StartsWith("RIFF"u8))
        {
            var form = h.Slice(8, 4);
            return form.SequenceEqual("WEBP"u8) ? "image/webp"
                : form.SequenceEqual("WAVE"u8) ? "audio/wav"
                : form.SequenceEqual("AVI "u8) ? "video/x-msvideo"
                : OctetStream;
        }

        if (h.Length >= 12 && h.Slice(4, 4).SequenceEqual("ftyp"u8))
        {
            return IsoMedia(h);
        }

        if (h.StartsWith(Ebml))
        {
            return h.IndexOf("webm"u8) >= 0 ? "video/webm" : "video/x-matroska";
        }

        if (h.StartsWith("%PDF-"u8))
        {
            return "application/pdf";
        }

        if (h.StartsWith(ZipLocal) || h.StartsWith(ZipEmpty))
        {
            // EPUB's first entry is an uncompressed "mimetype" holding its own type.
            return h.Length >= 58 && h.Slice(30, 8).SequenceEqual("mimetype"u8) && h.Slice(38, 20).SequenceEqual("application/epub+zip"u8)
                ? "application/epub+zip"
                : "application/zip";
        }

        if (h.StartsWith("ID3"u8))
        {
            return "audio/mpeg";
        }

        if (h.StartsWith("OggS"u8))
        {
            return "audio/ogg";
        }

        if (h.StartsWith("fLaC"u8))
        {
            return "audio/flac";
        }

        if (h.StartsWith(Gzip))
        {
            return "application/gzip";
        }

        if (h.StartsWith(SevenZip))
        {
            return "application/x-7z-compressed";
        }

        if (h.StartsWith(Rar))
        {
            return "application/vnd.rar";
        }

        if (h.StartsWith(TiffLittle) || h.StartsWith(TiffBig))
        {
            return "image/tiff";
        }

        if (h.StartsWith(Ico))
        {
            return "image/x-icon";
        }

        // "BM" is also how "BMW …" starts; the DIB header size that follows is what makes it a bitmap.
        if (h.StartsWith("BM"u8) && h.Length >= 18
            && BinaryPrimitives.ReadUInt32LittleEndian(h.Slice(14, 4)) is 12 or 40 or 52 or 56 or 108 or 124)
        {
            return "image/bmp";
        }

        if (h.StartsWith("MZ"u8))
        {
            return "application/x-msdownload";
        }

        if (h.StartsWith(Elf))
        {
            return "application/x-executable";
        }

        // Before the MPEG frame sync below, which FF FE would otherwise match.
        if (h.StartsWith(Utf16Big) || h.StartsWith(Utf16Little))
        {
            return "text/plain";
        }

        // An MPEG audio frame: eleven set sync bits. Layer 0 with the ADTS pattern is AAC; any other layer is MP3.
        if (h.Length >= 2 && h[0] == 0xFF && (h[1] & 0xE0) == 0xE0)
        {
            if ((h[1] & 0xF6) == 0xF0)
            {
                return "audio/aac";
            }

            if (((h[1] >> 1) & 0x03) != 0)
            {
                return "audio/mpeg";
            }
        }

        return null;
    }

    private static string IsoMedia(ReadOnlySpan<byte> h)
    {
        var major = h.Slice(8, 4);
        var boxSize = (int)Math.Min(BinaryPrimitives.ReadUInt32BigEndian(h), (uint)h.Length);

        if (major.SequenceEqual("M4A "u8))
        {
            return "audio/mp4";
        }

        if (major.SequenceEqual("qt  "u8))
        {
            return "video/quicktime";
        }

        if (HasBrand(h, boxSize, "avif"u8) || HasBrand(h, boxSize, "avis"u8))
        {
            return "image/avif";
        }

        if (HasBrand(h, boxSize, "heic"u8) || HasBrand(h, boxSize, "heix"u8) || HasBrand(h, boxSize, "hevc"u8)
            || HasBrand(h, boxSize, "heim"u8) || HasBrand(h, boxSize, "heis"u8))
        {
            return "image/heic";
        }

        if (HasBrand(h, boxSize, "mif1"u8) || HasBrand(h, boxSize, "msf1"u8))
        {
            return "image/heif";
        }

        if (major.SequenceEqual("M4V "u8) || major.SequenceEqual("isom"u8) || major.SequenceEqual("iso2"u8)
            || major.SequenceEqual("iso4"u8) || major.SequenceEqual("iso5"u8) || major.SequenceEqual("iso6"u8)
            || major.SequenceEqual("mp41"u8) || major.SequenceEqual("mp42"u8) || major.SequenceEqual("avc1"u8)
            || major.SequenceEqual("dash"u8) || major.SequenceEqual("mmp4"u8))
        {
            return "video/mp4";
        }

        return OctetStream;
    }

    private static bool HasBrand(ReadOnlySpan<byte> h, int boxSize, ReadOnlySpan<byte> brand)
    {
        if (h.Slice(8, 4).SequenceEqual(brand))
        {
            return true;
        }

        for (var i = 16; i + 4 <= boxSize; i += 4)
        {
            if (h.Slice(i, 4).SequenceEqual(brand))
            {
                return true;
            }
        }

        return false;
    }

    private static string? Markup(ReadOnlySpan<byte> h)
    {
        var i = h.StartsWith(Utf8Bom) ? Utf8Bom.Length : 0;
        while (i < h.Length && h[i] is 0x09 or 0x0A or 0x0C or 0x0D or 0x20)
        {
            i++;
        }

        var rest = h[i..];
        if (rest.IsEmpty || rest[0] != (byte)'<')
        {
            return null;
        }

        if (StartsWithTag(rest, "<SVG"u8))
        {
            return "image/svg+xml";
        }

        if (StartsWithIgnoreCase(rest, "<?XML"u8))
        {
            return ContainsIgnoreCase(rest, "<SVG"u8) ? "image/svg+xml" : "application/xml";
        }

        if (StartsWithIgnoreCase(rest, "<!--"u8))
        {
            return "text/html";
        }

        foreach (var tag in HtmlTags)
        {
            if (StartsWithTag(rest, tag))
            {
                return "text/html";
            }
        }

        return null;
    }

    private static bool StartsWithTag(ReadOnlySpan<byte> value, ReadOnlySpan<byte> tag) =>
        StartsWithIgnoreCase(value, tag)
        && (value.Length == tag.Length || value[tag.Length] is 0x20 or 0x3E or 0x09 or 0x0A or 0x0D or 0x0C or (byte)'/');

    private static bool StartsWithIgnoreCase(ReadOnlySpan<byte> value, ReadOnlySpan<byte> prefix) =>
        value.Length >= prefix.Length && Ascii.EqualsIgnoreCase(value[..prefix.Length], prefix);

    private static bool ContainsIgnoreCase(ReadOnlySpan<byte> value, ReadOnlySpan<byte> needle)
    {
        for (var i = 0; i + needle.Length <= value.Length; i++)
        {
            if (Ascii.EqualsIgnoreCase(value.Slice(i, needle.Length), needle))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsText(ReadOnlySpan<byte> h)
    {
        foreach (var b in h)
        {
            // WHATWG binary data bytes.
            if (b <= 0x08 || b == 0x0B || (b >= 0x0E && b <= 0x1A) || (b >= 0x1C && b <= 0x1F))
            {
                return false;
            }
        }

        if (Utf8.IsValid(h))
        {
            return true;
        }

        // The head may end part-way through a multi-byte character; that is not evidence of binary content.
        if (h.Length == HeadLength)
        {
            for (var cut = 1; cut <= 3; cut++)
            {
                if (Utf8.IsValid(h[..^cut]))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
