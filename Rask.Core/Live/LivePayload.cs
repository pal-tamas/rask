using System.Buffers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Rask.Core.Authentication;
using Rask.Core.Routing;
using Rask.Core.ScopedCss;

namespace Rask.Core.Live;

public static class LivePayload
{
    public static string InjectRootAttr(string html, string sessionId)
    {
        // Linear scan for the first "<body" (case-insensitive). Faster than a compiled regex
        // for the typical render path and avoids the regex engine's per-call state allocation.
        var i = IndexOfBodyOpen(html);
        if (i < 0)
        {
            return html;
        }

        var encoded = HtmlEncoder.Default.Encode(sessionId);
        var insertAt = i + "<body".Length;
        var sb = new StringBuilder(html.Length + encoded.Length + 32);
        sb.Append(html, 0, insertAt);
        sb.Append(" data-rask-root=\"").Append(encoded).Append('"');
        sb.Append(html, insertAt, html.Length - insertAt);
        return sb.ToString();
    }

    public static string ExtractBody(string html)
    {
        var open = IndexOfBodyOpen(html);
        if (open < 0)
        {
            return html;
        }

        // Find the matching '>' that closes the opening <body ...> tag, then look for </body>.
        var tagEnd = html.IndexOf('>', open);
        if (tagEnd < 0)
        {
            return html;
        }

        var close = IndexOfIgnoreCase(html, "</body>", tagEnd + 1);
        if (close < 0)
        {
            return html;
        }

        return html.Substring(open, close + "</body>".Length - open);
    }

    public static string BuildPayload(
        string html,
        string? historyUrl,
        bool replace,
        string? cssText = null,
        AuthInstruction? auth = null,
        PendingDownload? download = null)
    {
        // Used by the WASM host where the payload is handed to JS interop as a UTF-16 string.
        // The server host calls BuildPayloadUtf8 instead to skip the UTF-16 round-trip.
        var bytes = BuildPayloadUtf8(html, historyUrl, replace, cssText, auth, download);
        return Encoding.UTF8.GetString(bytes);
    }

    public static byte[] BuildPayloadUtf8(
        string html,
        string? historyUrl,
        bool replace,
        string? cssText = null,
        AuthInstruction? auth = null,
        PendingDownload? download = null)
    {
        var buffer = new ArrayBufferWriter<byte>(initialCapacity: 4096);
        using (var writer = new Utf8JsonWriter(buffer))
        {
            WriteJson(writer, html, historyUrl, replace, cssText, auth, download);
        }

        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>
    /// Server live-path payload builder. Encodes <paramref name="html"/> to UTF-8 once,
    /// locates the <c>&lt;body&gt;</c> / <c>&lt;/body&gt;</c> bounds on the byte span via
    /// vectorized <see cref="MemoryExtensions.IndexOf{T}(System.ReadOnlySpan{T}, T)"/> (no
    /// UTF-16 char-by-char scan), splices <c>data-rask-root="..."</c> on the opening tag,
    /// and writes the JSON payload containing **only the body**. Replaces the prior
    /// <see cref="InjectRootAttr"/> + <see cref="ExtractBody"/> + <see cref="BuildPayloadUtf8(string,string,bool,string,AuthInstruction,PendingDownload)"/>
    /// chain in one pass.
    /// </summary>
    public static byte[] BuildPayloadUtf8WithBody(
        string html,
        string sessionId,
        string? historyUrl,
        bool replace,
        string? cssText = null,
        AuthInstruction? auth = null,
        PendingDownload? download = null)
        => BuildPayloadUtf8Spliced(html, sessionId, includeOnlyBody: true,
            historyUrl, replace, cssText, auth, download);

    /// <summary>
    /// WASM live-path payload builder. Same UTF-8 splice as
    /// <see cref="BuildPayloadUtf8WithBody"/>, but emits the **whole document** (Doctype,
    /// Html, Head, Body) so the JS-side morph against <c>document.documentElement</c> can
    /// update head children too — title, stylesheet <c>&lt;link&gt;</c>s, the scoped-css
    /// link. The data-rask-root marker is still spliced onto the opening <c>&lt;body&gt;</c>.
    /// </summary>
    public static byte[] BuildPayloadUtf8WithRoot(
        string html,
        string sessionId,
        string? historyUrl,
        bool replace,
        string? cssText = null,
        AuthInstruction? auth = null,
        PendingDownload? download = null)
        => BuildPayloadUtf8Spliced(html, sessionId, includeOnlyBody: false,
            historyUrl, replace, cssText, auth, download);

    private static byte[] BuildPayloadUtf8Spliced(
        string html,
        string sessionId,
        bool includeOnlyBody,
        string? historyUrl,
        bool replace,
        string? cssText,
        AuthInstruction? auth,
        PendingDownload? download)
    {
        var htmlByteCount = Encoding.UTF8.GetByteCount(html);
        var htmlBuffer = ArrayPool<byte>.Shared.Rent(htmlByteCount);
        try
        {
            var written = Encoding.UTF8.GetBytes(html, htmlBuffer);
            var htmlBytes = htmlBuffer.AsSpan(0, written);

            const int bodyOpenLen = 5; // "<body"
            var bodyOpen = IndexOfBodyOpenUtf8(htmlBytes);
            if (bodyOpen < 0)
            {
                // No <body> tag — fall back to the string-side payload builder.
                return BuildPayloadUtf8(html, historyUrl, replace, cssText, auth, download);
            }

            var sliceStart = includeOnlyBody ? bodyOpen : 0;
            int sliceEnd;
            if (includeOnlyBody)
            {
                var tagEndRel = htmlBytes[bodyOpen..].IndexOf((byte)'>');
                if (tagEndRel < 0)
                {
                    return BuildPayloadUtf8(html, historyUrl, replace, cssText, auth, download);
                }

                var afterOpenTag = bodyOpen + tagEndRel + 1;
                var closeIdx = IndexOfIgnoreCaseUtf8(htmlBytes, "</body>"u8, afterOpenTag);
                if (closeIdx < 0)
                {
                    return BuildPayloadUtf8(html, historyUrl, replace, cssText, auth, download);
                }

                sliceEnd = closeIdx + "</body>"u8.Length;
            }
            else
            {
                sliceEnd = htmlBytes.Length;
            }

            var slice = htmlBytes[sliceStart..sliceEnd];
            var bodyOpenWithinSlice = bodyOpen - sliceStart;

            // Build the spliced output in a second rented buffer. Utf8JsonWriter.WriteString
            // needs a contiguous span — we cannot stream-write the parts.
            var encodedSessionId = HtmlEncoder.Default.Encode(sessionId);
            var sidByteCount = Encoding.UTF8.GetByteCount(encodedSessionId);

            var prefix = " data-rask-root=\""u8;
            var suffix = "\""u8;
            var splicedLen = slice.Length + prefix.Length + sidByteCount + suffix.Length;

            var splicedBuffer = ArrayPool<byte>.Shared.Rent(splicedLen);
            try
            {
                var spliced = splicedBuffer.AsSpan(0, splicedLen);
                var cursor = 0;
                // [0 .. bodyOpenWithinSlice + bodyOpenLen) — everything up to and including "<body".
                var headLen = bodyOpenWithinSlice + bodyOpenLen;
                slice[..headLen].CopyTo(spliced[cursor..]);
                cursor += headLen;
                prefix.CopyTo(spliced[cursor..]);
                cursor += prefix.Length;
                Encoding.UTF8.GetBytes(encodedSessionId, spliced[cursor..]);
                cursor += sidByteCount;
                suffix.CopyTo(spliced[cursor..]);
                cursor += suffix.Length;
                slice[headLen..].CopyTo(spliced[cursor..]);

                var output = new ArrayBufferWriter<byte>(initialCapacity: 4096);
                using (var writer = new Utf8JsonWriter(output))
                {
                    WriteJsonUtf8Body(writer, spliced, historyUrl, replace, cssText, auth, download);
                }

                return output.WrittenSpan.ToArray();
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(splicedBuffer);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(htmlBuffer);
        }
    }

    /// <summary>
    /// UTF-8 byte-span variant of <see cref="ExtractBody"/>. Returns a slice of the input —
    /// no allocation. If no <c>&lt;body&gt;</c> tag is present, returns the input unchanged.
    /// </summary>
    public static ReadOnlySpan<byte> ExtractBodyUtf8(ReadOnlySpan<byte> html)
    {
        var open = IndexOfBodyOpenUtf8(html);
        if (open < 0)
        {
            return html;
        }

        var tagEnd = html[open..].IndexOf((byte)'>');
        if (tagEnd < 0)
        {
            return html;
        }

        var afterTagEnd = open + tagEnd + 1;
        var close = IndexOfIgnoreCaseUtf8(html, "</body>"u8, afterTagEnd);
        if (close < 0)
        {
            return html;
        }

        return html.Slice(open, close + "</body>"u8.Length - open);
    }

    private static void WriteJson(
        Utf8JsonWriter writer,
        string html,
        string? historyUrl,
        bool replace,
        string? cssText,
        AuthInstruction? auth,
        PendingDownload? download)
    {
        var cssHash = ScopedCssRegistry.CurrentHash;

        writer.WriteStartObject();
        writer.WriteString("html", html);
        WriteJsonTail(writer, cssHash, historyUrl, replace, cssText, auth, download);
    }

    private static void WriteJsonUtf8Body(
        Utf8JsonWriter writer,
        ReadOnlySpan<byte> htmlUtf8,
        string? historyUrl,
        bool replace,
        string? cssText,
        AuthInstruction? auth,
        PendingDownload? download)
    {
        var cssHash = ScopedCssRegistry.CurrentHash;

        writer.WriteStartObject();
        writer.WriteString("html", htmlUtf8);
        WriteJsonTail(writer, cssHash, historyUrl, replace, cssText, auth, download);
    }

    private static void WriteJsonTail(
        Utf8JsonWriter writer,
        string? cssHash,
        string? historyUrl,
        bool replace,
        string? cssText,
        AuthInstruction? auth,
        PendingDownload? download)
    {
        writer.WriteString("cssHash", cssHash);

        if (cssText is not null)
        {
            writer.WriteString("cssText", cssText);
        }

        if (historyUrl is not null)
        {
            writer.WriteStartObject("history");
            writer.WriteString("action", replace ? "replace" : "push");
            writer.WriteString("url", historyUrl);
            writer.WriteEndObject();
        }

        if (auth is not null)
        {
            writer.WriteStartObject("auth");
            writer.WriteString("ticket", auth.Ticket);
            if (auth.ReturnUrl is not null)
            {
                writer.WriteString("returnUrl", auth.ReturnUrl);
            }

            writer.WriteEndObject();
        }

        if (download is not null)
        {
            writer.WriteStartObject("download");
            writer.WriteString("filename", download.Filename);
            if (download.ContentType is not null)
            {
                writer.WriteString("contentType", download.ContentType);
            }

            if (download.Url is not null)
            {
                writer.WriteString("url", download.Url);
            }

            if (download.Bytes is not null)
            {
                writer.WriteBase64String("bytes", download.Bytes);
            }

            writer.WriteEndObject();
        }

        writer.WriteEndObject();
    }

    private static int IndexOfBodyOpen(string html)
    {
        // Case-insensitive scan for "<body" followed by a tag boundary character (space, >, /,
        // or end-of-string). Matches the regex `<body\b` shape without an engine allocation.
        const string token = "<body";
        var end = html.Length - token.Length;
        for (var i = 0; i <= end; i++)
        {
            if (!MatchesIgnoreCase(html, i, token))
            {
                continue;
            }

            var after = i + token.Length;
            if (after == html.Length)
            {
                return i;
            }

            var c = html[after];
            if (c == ' ' || c == '\t' || c == '\r' || c == '\n' || c == '>' || c == '/')
            {
                return i;
            }
        }

        return -1;
    }

    private static int IndexOfIgnoreCase(string source, string value, int startIndex)
    {
        var end = source.Length - value.Length;
        for (var i = startIndex; i <= end; i++)
        {
            if (MatchesIgnoreCase(source, i, value))
            {
                return i;
            }
        }

        return -1;
    }

    private static bool MatchesIgnoreCase(string source, int sourceIndex, string value)
    {
        for (var j = 0; j < value.Length; j++)
        {
            var a = source[sourceIndex + j];
            var b = value[j];
            if (a == b) continue;
            if (a >= 'A' && a <= 'Z') a = (char)(a + 32);
            if (b >= 'A' && b <= 'Z') b = (char)(b + 32);
            if (a != b) return false;
        }

        return true;
    }

    internal static int IndexOfBodyOpenUtf8(ReadOnlySpan<byte> html)
    {
        // Scan UTF-8 bytes for "<body" followed by a tag boundary. Uses MemoryExtensions.IndexOf
        // for the initial '<' search (vectorized via Vector128/Vector256 on supported hardware).
        ReadOnlySpan<byte> bodyName = "body"u8;
        var offset = 0;
        while (true)
        {
            var rel = html[offset..].IndexOf((byte)'<');
            if (rel < 0)
            {
                return -1;
            }

            offset += rel;
            var after = offset + 1;
            if (after + bodyName.Length > html.Length)
            {
                return -1;
            }

            if (AsciiEqualsIgnoreCaseUtf8(html.Slice(after, bodyName.Length), bodyName))
            {
                var boundary = after + bodyName.Length;
                if (boundary == html.Length)
                {
                    return offset;
                }

                var c = html[boundary];
                if (c == (byte)' ' || c == (byte)'\t' || c == (byte)'\r' || c == (byte)'\n'
                    || c == (byte)'>' || c == (byte)'/')
                {
                    return offset;
                }
            }

            offset++;
            if (offset >= html.Length)
            {
                return -1;
            }
        }
    }

    internal static int IndexOfIgnoreCaseUtf8(ReadOnlySpan<byte> source, ReadOnlySpan<byte> value, int startIndex)
    {
        // For an all-ASCII needle, vectorize the first-byte scan and verify the remaining bytes
        // case-insensitively. Falls back to linear scan if the needle's first byte isn't ASCII
        // letter — not the case for any caller today, but kept for safety.
        if (value.Length == 0)
        {
            return startIndex;
        }

        var first = value[0];
        var firstUpper = (first >= (byte)'a' && first <= (byte)'z') ? (byte)(first - 32) : first;
        var firstLower = (first >= (byte)'A' && first <= (byte)'Z') ? (byte)(first + 32) : first;
        var end = source.Length - value.Length;
        var i = startIndex;
        while (i <= end)
        {
            var rel = (firstUpper == firstLower)
                ? source[i..].IndexOf(first)
                : source[i..].IndexOfAny(firstUpper, firstLower);
            if (rel < 0)
            {
                return -1;
            }

            var candidate = i + rel;
            if (candidate > end)
            {
                return -1;
            }

            if (AsciiEqualsIgnoreCaseUtf8(source.Slice(candidate, value.Length), value))
            {
                return candidate;
            }

            i = candidate + 1;
        }

        return -1;
    }

    private static bool AsciiEqualsIgnoreCaseUtf8(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
    {
        if (a.Length != b.Length)
        {
            return false;
        }

        for (var j = 0; j < a.Length; j++)
        {
            var x = a[j];
            var y = b[j];
            if (x == y) continue;
            if (x >= (byte)'A' && x <= (byte)'Z') x = (byte)(x + 32);
            if (y >= (byte)'A' && y <= (byte)'Z') y = (byte)(y + 32);
            if (x != y) return false;
        }

        return true;
    }
}
