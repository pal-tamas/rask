using System.Text;
using Rask.Storage.Upload;

namespace Rask.Storage.Tests;

public sealed class ContentSnifferTests
{
    public static TheoryData<string, byte[]> Signatures => new()
    {
        { "image/png", Samples.Png(32) },
        { "image/jpeg", [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46] },
        { "image/gif", "GIF89a\u0001\u0000"u8.ToArray() },
        { "image/webp", "RIFF\u0000\u0000\u0000\u0000WEBPVP8 "u8.ToArray() },
        { "audio/wav", "RIFF\u0000\u0000\u0000\u0000WAVEfmt "u8.ToArray() },
        { "image/avif", Ftyp("avif", "mif1", "miaf") },
        { "image/avif", Ftyp("mif1", "avif") },
        { "image/heic", Ftyp("heic", "mif1") },
        { "video/mp4", Ftyp("isom", "iso2", "avc1") },
        { "audio/mp4", Ftyp("M4A ", "isom") },
        { "video/quicktime", Ftyp("qt  ") },
        { "video/webm", [0x1A, 0x45, 0xDF, 0xA3, 0x42, 0x82, 0x84, (byte)'w', (byte)'e', (byte)'b', (byte)'m'] },
        { "video/x-matroska", [0x1A, 0x45, 0xDF, 0xA3, 0x42, 0x82, 0x88, (byte)'m', (byte)'a', (byte)'t', (byte)'r'] },
        { "audio/mpeg", "ID3\u0004\u0000"u8.ToArray() },
        { "audio/mpeg", [0xFF, 0xFB, 0x90, 0x64] },
        { "audio/aac", [0xFF, 0xF1, 0x50, 0x80] },
        { "audio/ogg", "OggS\u0000"u8.ToArray() },
        { "audio/flac", "fLaC\u0000"u8.ToArray() },
        { "application/pdf", "%PDF-1.7\n"u8.ToArray() },
        { "application/zip", [0x50, 0x4B, 0x03, 0x04, 0x14, 0x00] },
        { "application/gzip", [0x1F, 0x8B, 0x08] },
        { "image/tiff", [0x49, 0x49, 0x2A, 0x00] },
        { "application/x-msdownload", "MZ\u0090\u0000"u8.ToArray() },
        { "application/x-executable", [0x7F, 0x45, 0x4C, 0x46, 0x02] },
    };

    [Theory]
    [MemberData(nameof(Signatures))]
    public void A_signature_names_its_type(string expected, byte[] head) =>
        Assert.Equal(expected, ContentSniffer.Sniff(head));

    [Theory]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"/>", "image/svg+xml")]
    [InlineData("\uFEFF  \n<SVG>", "image/svg+xml")]
    [InlineData("<?xml version=\"1.0\"?>\n<svg xmlns=\"http://www.w3.org/2000/svg\"/>", "image/svg+xml")]
    [InlineData("<?xml version=\"1.0\"?><feed/>", "application/xml")]
    [InlineData("<!DOCTYPE html><p>", "text/html")]
    [InlineData("\r\n\t<HTML>", "text/html")]
    [InlineData("<script>alert(1)</script>", "text/html")]
    [InlineData("<!-- a comment -->", "text/html")]
    [InlineData("<b>bold</b>", "text/html")]
    public void Markup_a_browser_would_render_is_named_as_markup(string content, string expected) =>
        Assert.Equal(expected, ContentSniffer.Sniff(Encoding.UTF8.GetBytes(content)));

    [Fact]
    public void A_tag_needs_its_terminator_so_a_word_starting_with_one_is_text() =>
        Assert.Equal("text/plain", ContentSniffer.Sniff("<bravo is not a tag"u8));

    [Fact]
    public void Utf8_text_is_text_and_binary_is_not()
    {
        Assert.Equal("text/plain", ContentSniffer.Sniff("árvíztűrő tükörfúrógép"u8));
        Assert.Equal(ContentSniffer.OctetStream, ContentSniffer.Sniff([0x00, 0x01, 0x02, 0x03]));
        Assert.Equal(ContentSniffer.OctetStream, ContentSniffer.Sniff([]));
    }

    [Fact]
    public void A_head_cut_mid_character_is_still_text()
    {
        var text = Encoding.UTF8.GetBytes(new string('é', ContentSniffer.HeadLength));
        Assert.Equal("text/plain", ContentSniffer.Sniff(text.AsSpan(0, ContentSniffer.HeadLength - 1).ToArray().Concat(new byte[] { 0xC3 }).ToArray()));
    }

    [Fact]
    public void A_gif_polyglot_carrying_html_is_a_gif()
    {
        // Safe because an image cannot run script — which is ContentTypePolicy's job, not this one's.
        var polyglot = "GIF89a<html><script>alert(1)</script>"u8.ToArray();
        Assert.Equal("image/gif", ContentSniffer.Sniff(polyglot));
    }

    [Fact]
    public void Utf16_is_text_not_an_mpeg_frame() =>
        Assert.Equal("text/plain", ContentSniffer.Sniff([0xFF, 0xFE, 0x3C, 0x00]));

    private static byte[] Ftyp(string major, params string[] compatible)
    {
        var size = 16 + (4 * compatible.Length);
        var box = new byte[Math.Max(size, 16)];
        box[3] = (byte)size;
        "ftyp"u8.CopyTo(box.AsSpan(4));
        Encoding.ASCII.GetBytes(major).CopyTo(box, 8);
        for (var i = 0; i < compatible.Length; i++)
        {
            Encoding.ASCII.GetBytes(compatible[i]).CopyTo(box, 16 + (4 * i));
        }

        return box;
    }
}

public sealed class ContentTypePolicyTests
{
    [Theory]
    [InlineData("image/png", true)]
    [InlineData("video/mp4", true)]
    [InlineData("audio/mpeg", true)]
    [InlineData("image/svg+xml", false)]
    [InlineData("text/html", false)]
    [InlineData("application/pdf", false)]
    [InlineData("text/plain", false)]
    [InlineData("image/tiff", false)]
    public void Only_raster_images_audio_and_video_are_inline(string type, bool inline) =>
        Assert.Equal(inline, ContentTypePolicy.IsInline(type));

    [Theory]
    [InlineData("text/html")]
    [InlineData("image/svg+xml")]
    [InlineData("application/xml")]
    [InlineData("application/xhtml+xml")]
    public void Script_capable_types_are_served_as_octet_stream(string type) =>
        Assert.Equal("application/octet-stream", ContentTypePolicy.ServedType(type));

    [Fact]
    public void Everything_else_is_served_as_itself() =>
        Assert.Equal("application/pdf", ContentTypePolicy.ServedType("application/pdf"));

    [Theory]
    [InlineData("text/plain", "data.csv", "text/csv")]
    [InlineData("text/plain", "notes.MD", "text/markdown")]
    [InlineData("application/zip", "report.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    [InlineData("text/plain", "evil.png", "text/plain")]
    [InlineData("text/html", "evil.png", "text/html")]
    [InlineData("text/html", "page.csv", "text/html")]
    [InlineData("application/octet-stream", "image.jpg", "application/octet-stream")]
    public void A_name_only_narrows_what_the_bytes_already_said(string sniffed, string name, string expected) =>
        Assert.Equal(expected, ContentTypePolicy.Refine(sniffed, name));

    [Fact]
    public void AllowedTypes_match_exactly_or_by_family_and_empty_allows_anything()
    {
        Assert.True(ContentTypePolicy.IsAllowed("application/pdf", []));
        Assert.True(ContentTypePolicy.IsAllowed("image/png", ["image/*"]));
        Assert.True(ContentTypePolicy.IsAllowed("IMAGE/PNG", ["image/png"]));
        Assert.False(ContentTypePolicy.IsAllowed("text/html", ["image/*", "application/pdf"]));
        Assert.False(ContentTypePolicy.IsAllowed("imagery/png", ["image/*"]));
    }
}

public sealed class SafeFileNameTests
{
    [Theory]
    [InlineData("../../etc/passwd", "passwd")]
    [InlineData("C:\\Users\\me\\report.pdf", "report.pdf")]
    [InlineData("a/b/c.txt", "c.txt")]
    [InlineData("in\u0000voice\r\n.pdf", "invoice.pdf")]
    [InlineData("say \"hi\".txt", "say hi.txt")]
    [InlineData("", "file")]
    [InlineData(null, "file")]
    [InlineData("..", "file")]
    [InlineData("dir/", "file")]
    public void A_name_is_reduced_to_a_safe_leaf(string? input, string expected) =>
        Assert.Equal(expected, SafeFileName.Clean(input));

    [Fact]
    public void Bidi_overrides_are_removed_so_an_extension_cannot_be_disguised() =>
        Assert.Equal("invoicefdp.exe", SafeFileName.Clean("invoice\u202Efdp.exe"));

    [Fact]
    public void A_name_is_normalized_to_one_spelling() =>
        Assert.Equal("\u00E9.txt", SafeFileName.Clean("e\u0301.txt"));

    [Fact]
    public void A_long_name_is_capped_without_splitting_a_surrogate_pair()
    {
        var name = new string('a', 254) + "\U0001F600" + "tail";
        var cleaned = SafeFileName.Clean(name);

        Assert.True(cleaned.Length <= SafeFileName.MaxLength);
        Assert.False(char.IsHighSurrogate(cleaned[^1]));
    }

    [Fact]
    public void A_lone_surrogate_is_dropped() =>
        Assert.Equal("ab.txt", SafeFileName.Clean("a\uD800b.txt"));
}

public sealed class KeyLayoutTests
{
    [Theory]
    [InlineData(true, "app/public/3f/3f2a0000000000000000000000000001")]
    [InlineData(false, "app/private/3f/3f2a0000000000000000000000000001")]
    public void A_key_is_derived_from_the_id_and_its_visibility_and_parses_back(bool isPublic, string expected)
    {
        var id = Guid.Parse("3f2a0000-0000-0000-0000-000000000001");
        var key = KeyLayout.KeyOf("app/", id, isPublic);

        Assert.Equal(expected, key);
        Assert.True(KeyLayout.TryParse(key, "app/", out var parsed));
        Assert.Equal(id, parsed);
    }

    [Theory]
    [InlineData("other/public/3f/3f2a0000000000000000000000000001")]
    [InlineData("app/3f/3f2a0000000000000000000000000001")]
    [InlineData("app/shared/3f/3f2a0000000000000000000000000001")]
    [InlineData("app/public/aa/3f2a0000000000000000000000000001")]
    [InlineData("app/public/3f/3F2A0000000000000000000000000001")]
    [InlineData("app/private/3f/3f2a00000000000000000000000000")]
    [InlineData("app/backup.sql")]
    public void Anything_not_in_the_layout_is_foreign(string key) =>
        Assert.False(KeyLayout.TryParse(key, "app/", out _));

    [Theory]
    [InlineData("ab/abc", true)]
    [InlineData("a.b_c-d/e", true)]
    [InlineData("", false)]
    [InlineData("/ab", false)]
    [InlineData("ab//c", false)]
    [InlineData("ab/../c", false)]
    [InlineData("ab/./c", false)]
    [InlineData("AB/c", false)]
    [InlineData("ab\\c", false)]
    [InlineData("ab/c%2F", false)]
    public void Only_plain_segments_are_keys(string key, bool valid) =>
        Assert.Equal(valid, KeyLayout.IsValid(key));
}
