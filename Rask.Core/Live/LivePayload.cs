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
}
