using System.Buffers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Rask.Core.Authentication;
using Rask.Core.Routing;

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
        var sb = RaskStringBuilderPool.Shared.Get();
        try
        {
            sb.EnsureCapacity(html.Length + encoded.Length + 32);
            sb.Append(html, 0, insertAt);
            sb.Append(" data-rask-root=\"").Append(encoded).Append('"');
            sb.Append(html, insertAt, html.Length - insertAt);
            return sb.ToString();
        }
        finally
        {
            RaskStringBuilderPool.Shared.Return(sb);
        }
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
        PendingDownload? download = null,
        string? jsText = null,
        IReadOnlyList<PendingJsInvoke>? jsInvokes = null)
    {
        // Used by the WASM host where the payload is handed to JS interop as a UTF-16 string.
        // The server host calls BuildPayloadUtf8 instead to skip the UTF-16 round-trip.
        var bytes = BuildPayloadUtf8(html, historyUrl, replace, cssText, auth, download, jsText, jsInvokes);
        return Encoding.UTF8.GetString(bytes);
    }

    public static byte[] BuildPayloadUtf8(
        string html,
        string? historyUrl,
        bool replace,
        string? cssText = null,
        AuthInstruction? auth = null,
        PendingDownload? download = null,
        string? jsText = null,
        IReadOnlyList<PendingJsInvoke>? jsInvokes = null)
    {
        var buffer = new ArrayBufferWriter<byte>(4096);
        BuildPayloadUtf8(buffer, html, historyUrl, replace, cssText, auth, download, jsText, jsInvokes);
        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>
    ///     Pooled-writer overload of
    ///     <see
    ///         cref="BuildPayloadUtf8(string,string,bool,string,AuthInstruction,PendingDownload,string,IReadOnlyList{ScopedJsInvoke},IReadOnlyList{PendingJsInvoke})" />
    ///     .
    ///     Writes the JSON payload into the caller-supplied buffer; callers reuse the writer
    ///     across frames (Clear / ResetWrittenCount) to avoid the per-frame 4 KiB allocation.
    /// </summary>
    public static void BuildPayloadUtf8(
        ArrayBufferWriter<byte> output,
        string html,
        string? historyUrl,
        bool replace,
        string? cssText = null,
        AuthInstruction? auth = null,
        PendingDownload? download = null,
        string? jsText = null,
        IReadOnlyList<PendingJsInvoke>? jsInvokes = null)
    {
        using var writer = new Utf8JsonWriter(output, DiffWriterOptions);
        WriteJson(writer, html, historyUrl, replace, cssText, auth, download, jsText, jsInvokes);
    }

    /// <summary>
    ///     Server live-path payload builder. Encodes <paramref name="html" /> to UTF-8 once,
    ///     locates the <c>&lt;body&gt;</c> / <c>&lt;/body&gt;</c> bounds on the byte span via
    ///     vectorized <see cref="MemoryExtensions.IndexOf{T}(System.ReadOnlySpan{T}, T)" /> (no
    ///     UTF-16 char-by-char scan), splices <c>data-rask-root="..."</c> on the opening tag,
    ///     and writes the JSON payload containing **only the body**. Replaces the prior
    ///     <see cref="InjectRootAttr" /> + <see cref="ExtractBody" /> +
    ///     <see cref="BuildPayloadUtf8(string,string,bool,string,AuthInstruction,PendingDownload)" />
    ///     chain in one pass.
    /// </summary>
    public static byte[] BuildPayloadUtf8WithBody(
        string html,
        string sessionId,
        string? historyUrl,
        bool replace,
        string? cssText = null,
        AuthInstruction? auth = null,
        PendingDownload? download = null,
        string? jsText = null,
        IReadOnlyList<PendingJsInvoke>? jsInvokes = null)
    {
        var output = new ArrayBufferWriter<byte>(4096);
        BuildPayloadUtf8WithBody(output, html, sessionId, historyUrl, replace, cssText, auth, download, jsText,
            jsInvokes);
        return output.WrittenSpan.ToArray();
    }

    /// <summary>
    ///     Pooled-writer overload of
    ///     <see cref="BuildPayloadUtf8WithBody(string,string,string,bool,string,AuthInstruction,PendingDownload)" />.
    ///     Writes the JSON payload into <paramref name="output" />; the caller owns the buffer
    ///     and is expected to <c>ResetWrittenCount()</c> between frames so the rented array is
    ///     reused. Lets
    ///     <see
    ///         cref="System.Net.WebSockets.WebSocket.SendAsync(ReadOnlyMemory{byte}, System.Net.WebSockets.WebSocketMessageType, bool, CancellationToken)" />
    ///     consume <see cref="ArrayBufferWriter{T}.WrittenMemory" /> directly — no per-frame copy.
    /// </summary>
    public static void BuildPayloadUtf8WithBody(
        ArrayBufferWriter<byte> output,
        string html,
        string sessionId,
        string? historyUrl,
        bool replace,
        string? cssText = null,
        AuthInstruction? auth = null,
        PendingDownload? download = null,
        string? jsText = null,
        IReadOnlyList<PendingJsInvoke>? jsInvokes = null)
        => BuildPayloadUtf8Spliced(output, html, sessionId, true,
            historyUrl, replace, cssText, auth, download, jsText, jsInvokes);

    /// <summary>
    ///     WASM live-path payload builder. Same UTF-8 splice as
    ///     <see cref="BuildPayloadUtf8WithBody(string,string,string,bool,string,AuthInstruction,PendingDownload)" />,
    ///     but emits the **whole document** (Doctype, Html, Head, Body) so the JS-side morph
    ///     against <c>document.documentElement</c> can update head children too — title,
    ///     stylesheet <c>&lt;link&gt;</c>s, the scoped-css link. The data-rask-root marker is
    ///     still spliced onto the opening <c>&lt;body&gt;</c>.
    /// </summary>
    public static byte[] BuildPayloadUtf8WithRoot(
        string html,
        string sessionId,
        string? historyUrl,
        bool replace,
        string? cssText = null,
        AuthInstruction? auth = null,
        PendingDownload? download = null,
        string? jsText = null,
        IReadOnlyList<PendingJsInvoke>? jsInvokes = null)
    {
        var output = new ArrayBufferWriter<byte>(4096);
        BuildPayloadUtf8WithRoot(output, html, sessionId, historyUrl, replace, cssText, auth, download, jsText,
            jsInvokes);
        return output.WrittenSpan.ToArray();
    }

    /// <summary>
    ///     Pooled-writer overload of
    ///     <see cref="BuildPayloadUtf8WithRoot(string,string,string,bool,string,AuthInstruction,PendingDownload)" />.
    /// </summary>
    public static void BuildPayloadUtf8WithRoot(
        ArrayBufferWriter<byte> output,
        string html,
        string sessionId,
        string? historyUrl,
        bool replace,
        string? cssText = null,
        AuthInstruction? auth = null,
        PendingDownload? download = null,
        string? jsText = null,
        IReadOnlyList<PendingJsInvoke>? jsInvokes = null)
        => BuildPayloadUtf8Spliced(output, html, sessionId, false,
            historyUrl, replace, cssText, auth, download, jsText, jsInvokes);

    /// <summary>
    ///     Diff-mode payload: writes <c>{ "kind": "diff", "ops": [...] }</c> directly
    ///     into <paramref name="output" />. Each op is a positional JSON array whose
    ///     shape is fixed per <see cref="EditOpKind" /> — the client dispatches on
    ///     <c>op[0]</c> (the kind) and reads the remaining slots by position:
    ///     <code>
    ///         SetAttribute      [k, path[], name, value]
    ///         RemoveAttribute   [k, path[], name]
    ///         UpdateText        [k, path[], value]
    ///         InsertSubtree     [k, path[], html, domCount]
    ///         RemoveSubtree     [k, path[], domCount]
    ///         MoveSubtree       [k, path[], sourceSlot]
    ///     </code>
    ///     vs the prior <c>{"k":..,"p":..,"n":..,"v":..,"l":..}</c> object shape this
    ///     drops the four key strings (<c>k</c>, <c>n</c>, <c>v</c>, <c>l</c>) and the
    ///     <c>p</c> key — ~10–15 bytes/op savings depending on which fields applied.
    ///     The <c>"kind":"diff"</c> envelope field stays so the client's top-level
    ///     dispatcher (which also routes full-HTML payloads with <c>"kind":"html"</c>)
    ///     keeps the same branch shape.
    /// </summary>
    private static void WriteInternedOrString(Utf8JsonWriter writer, string? name, Dictionary<string, int>? nameIndex)
    {
        // null name → JSON null (matches the prior shape; only RemoveAttribute carries a
        // null Value, never a null Name in practice — but defensive against malformed ops).
        if (name is null)
        {
            writer.WriteNullValue();
            return;
        }

        if (nameIndex is not null && nameIndex.TryGetValue(name, out var idx))
        {
            writer.WriteNumberValue(idx);
            return;
        }

        writer.WriteStringValue(name);
    }

    // The Utf8JsonWriter default encoder is HTML-safe — it rewrites `<`, `>`, `&`, `+`,
    // `'`, and a long list of other characters to `\uXXXX` escapes so the JSON can be
    // embedded inside an HTML <script> tag without prematurely closing it. Diff payloads
    // never appear inline in HTML (they flow over the WebSocket and are decoded by
    // JSON.parse), and InsertSubtree ops carry whole HTML fragments where every `<` and
    // `>` would otherwise pay a 5× byte tax. UnsafeRelaxedJsonEscaping only escapes the
    // JSON-required characters (`"`, `\`, control bytes) — JSON.parse on the client
    // produces the identical string either way, so this is a pure wire-size win.
    private static readonly JsonWriterOptions DiffWriterOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static void BuildPayloadUtf8Diff(
        ArrayBufferWriter<byte> output,
        IReadOnlyList<EditOp> ops,
        string? historyUrl = null,
        bool replace = false)
    {
        // Pass 1: build the attribute-name symbol table. Intern when the name appears
        // 3+ times — break-even with the table overhead lands around there for typical
        // attribute names. Two occurrences of a short name like "class" cost more in the
        // table slot (`,"names":["class"]` ≈ 18 bytes) than they save in the two op refs
        // (~12 bytes saved). Three plus is comfortably net-positive for any name length.
        // Result: scenarios like AttributeBurstUpdate (100 ops sharing one name) drop
        // the duplicate name to a single integer per op (~1.2 KB saved); small diffs
        // pay no extra envelope.
        Dictionary<string, int>? nameIndex = null;
        Dictionary<string, int>? nameCount = null;
        for (var i = 0; i < ops.Count; i++)
        {
            var op = ops[i];
            if (op.Name is null) continue;
            if (op.Kind != EditOpKind.SetAttribute && op.Kind != EditOpKind.RemoveAttribute) continue;
            nameCount ??= new Dictionary<string, int>(StringComparer.Ordinal);
            nameCount.TryGetValue(op.Name, out var c);
            nameCount[op.Name] = c + 1;
        }

        List<string>? internedNames = null;
        if (nameCount is not null)
        {
            foreach (var kv in nameCount)
            {
                if (kv.Value < 3) continue;
                nameIndex ??= new Dictionary<string, int>(StringComparer.Ordinal);
                internedNames ??= new List<string>();
                nameIndex[kv.Key] = internedNames.Count;
                internedNames.Add(kv.Key);
            }
        }

        using var writer = new Utf8JsonWriter(output, DiffWriterOptions);
        writer.WriteStartObject();
        writer.WriteString("kind", "diff");

        if (internedNames is { Count: > 0 })
        {
            writer.WriteStartArray("names");
            foreach (var n in internedNames)
            {
                writer.WriteStringValue(n);
            }
            writer.WriteEndArray();
        }

        writer.WriteStartArray("ops");
        foreach (var op in ops)
        {
            writer.WriteStartArray();
            writer.WriteNumberValue((int)op.Kind);

            writer.WriteStartArray();
            foreach (var step in op.Path)
            {
                writer.WriteNumberValue(step);
            }
            writer.WriteEndArray();

            switch (op.Kind)
            {
                case EditOpKind.SetAttribute:
                    WriteInternedOrString(writer, op.Name, nameIndex);
                    if (op.Value is null) writer.WriteNullValue();
                    else writer.WriteStringValue(op.Value);
                    break;
                case EditOpKind.RemoveAttribute:
                    WriteInternedOrString(writer, op.Name, nameIndex);
                    break;
                case EditOpKind.UpdateText:
                    if (op.Value is null) writer.WriteNullValue();
                    else writer.WriteStringValue(op.Value);
                    break;
                case EditOpKind.InsertSubtree:
                    if (op.Value is null) writer.WriteNullValue();
                    else writer.WriteStringValue(op.Value);
                    writer.WriteNumberValue(op.Length);
                    break;
                case EditOpKind.RemoveSubtree:
                case EditOpKind.MoveSubtree:
                    writer.WriteNumberValue(op.Length);
                    break;
            }

            writer.WriteEndArray();
        }

        writer.WriteEndArray();

        if (historyUrl is not null)
        {
            writer.WriteStartObject("history");
            writer.WriteString("action", replace ? "replace" : "push");
            writer.WriteString("url", historyUrl);
            writer.WriteEndObject();
        }

        writer.WriteEndObject();
    }

    private static void BuildPayloadUtf8Spliced(
        ArrayBufferWriter<byte> output,
        string html,
        string sessionId,
        bool includeOnlyBody,
        string? historyUrl,
        bool replace,
        string? cssText,
        AuthInstruction? auth,
        PendingDownload? download,
        string? jsText,
        IReadOnlyList<PendingJsInvoke>? jsInvokes)
    {
        // Find <body> bounds on the UTF-16 source. The prior implementation
        // rented + encoded the entire html to UTF-8 first, scanned the byte span,
        // then rented a SECOND buffer for the spliced output — two rents and an
        // extra full-buffer copy per render. Scanning UTF-16 directly via
        // IndexOfBodyOpen/IndexOfIgnoreCase (both ASCII-needle, vectorised through
        // string.IndexOf paths) keeps the same matching semantics and lets us
        // encode straight into one buffer.
        const int bodyOpenLen = 5; // "<body"
        var bodyOpenChar = IndexOfBodyOpen(html);
        if (bodyOpenChar < 0)
        {
            BuildPayloadUtf8(output, html, historyUrl, replace, cssText, auth, download, jsText, jsInvokes);
            return;
        }

        int sliceStartChar = includeOnlyBody ? bodyOpenChar : 0;
        int sliceEndChar;
        if (includeOnlyBody)
        {
            var tagEndRel = html.AsSpan(bodyOpenChar).IndexOf('>');
            if (tagEndRel < 0)
            {
                BuildPayloadUtf8(output, html, historyUrl, replace, cssText, auth, download, jsText, jsInvokes);
                return;
            }

            var afterOpenTagChar = bodyOpenChar + tagEndRel + 1;
            var closeCharIdx = IndexOfIgnoreCase(html, "</body>", afterOpenTagChar);
            if (closeCharIdx < 0)
            {
                BuildPayloadUtf8(output, html, historyUrl, replace, cssText, auth, download, jsText, jsInvokes);
                return;
            }

            sliceEndChar = closeCharIdx + "</body>".Length;
        }
        else
        {
            sliceEndChar = html.Length;
        }

        // The splice point is right after "<body". Encode three slices into one
        // pooled UTF-8 buffer:
        //   1. html[sliceStartChar .. bodyOpenChar + "<body".Length)   (head incl. "<body")
        //   2. " data-rask-root=\"{encodedSessionId}\""                (the injection)
        //   3. html[bodyOpenChar + "<body".Length .. sliceEndChar)     (tail)
        var headEndChar = bodyOpenChar + bodyOpenLen;
        var headSlice = html.AsSpan(sliceStartChar, headEndChar - sliceStartChar);
        var tailSlice = html.AsSpan(headEndChar, sliceEndChar - headEndChar);

        var encodedSessionId = HtmlEncoder.Default.Encode(sessionId);
        var prefix = " data-rask-root=\""u8;
        var suffix = "\""u8;

        var headByteCount = Encoding.UTF8.GetByteCount(headSlice);
        var tailByteCount = Encoding.UTF8.GetByteCount(tailSlice);
        var sidByteCount = Encoding.UTF8.GetByteCount(encodedSessionId);
        var totalBytes = headByteCount + prefix.Length + sidByteCount + suffix.Length + tailByteCount;

        var buffer = ArrayPool<byte>.Shared.Rent(totalBytes);
        try
        {
            var span = buffer.AsSpan(0, totalBytes);
            var cursor = 0;
            cursor += Encoding.UTF8.GetBytes(headSlice, span[cursor..]);
            prefix.CopyTo(span[cursor..]);
            cursor += prefix.Length;
            cursor += Encoding.UTF8.GetBytes(encodedSessionId, span[cursor..]);
            suffix.CopyTo(span[cursor..]);
            cursor += suffix.Length;
            cursor += Encoding.UTF8.GetBytes(tailSlice, span[cursor..]);

            // Same relaxed encoder as the diff path — the WS payload is parsed by JSON.parse,
            // not embedded into HTML, so the default HTML-safe escaping inflates the "html"
            // field's `<` / `>` 5× for no security benefit. Shaves ~3-5 KB off a 10 KB page.
            using var writer = new Utf8JsonWriter(output, DiffWriterOptions);
            WriteJsonUtf8Body(writer, span[..cursor], historyUrl, replace, cssText, auth, download, jsText, jsInvokes);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    ///     UTF-8 byte-span variant of <see cref="ExtractBody" />. Returns a slice of the input —
    ///     no allocation. If no <c>&lt;body&gt;</c> tag is present, returns the input unchanged.
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
        PendingDownload? download,
        string? jsText,
        IReadOnlyList<PendingJsInvoke>? jsInvokes)
    {
        // cssText / jsText are vestigial parameters preserved for ABI; per-component
        // scoped assets now reach the client via <link>/<script> tags spliced into the
        // rendered HTML (HeadAssetRegistry.EmitMountedAssets) and served from
        // /_rask/a/{hash}.{ext}. Nothing in the live payload carries scoped CSS/JS
        // bytes or a global bundle hash anymore.
        _ = cssText;
        _ = jsText;
        writer.WriteStartObject();
        writer.WriteString("html", html);
        WriteJsonTail(writer, historyUrl, replace, auth, download, jsInvokes);
    }

    private static void WriteJsonUtf8Body(
        Utf8JsonWriter writer,
        ReadOnlySpan<byte> htmlUtf8,
        string? historyUrl,
        bool replace,
        string? cssText,
        AuthInstruction? auth,
        PendingDownload? download,
        string? jsText,
        IReadOnlyList<PendingJsInvoke>? jsInvokes)
    {
        _ = cssText;
        _ = jsText;
        writer.WriteStartObject();
        writer.WriteString("html", htmlUtf8);
        WriteJsonTail(writer, historyUrl, replace, auth, download, jsInvokes);
    }

    private static void WriteJsonTail(
        Utf8JsonWriter writer,
        string? historyUrl,
        bool replace,
        AuthInstruction? auth,
        PendingDownload? download,
        IReadOnlyList<PendingJsInvoke>? jsInvokes)
    {
        if (jsInvokes is { Count: > 0 })
        {
            // IJSRuntime.InvokeAsync<T> queue. Each entry resolves a dotted identifier
            // against `window` on the client (e.g. "sessionStorage.getItem"), invokes
            // it with the args (already JSON-encoded by the JSRuntime base class), and
            // ships the result back as { type: "jsResult", id, success, result|error }.
            // resultType drives how the client handles the return value (0=Default,
            // 1=JSVoidResult, 2=JSObjectReference, 3=JSStreamReference); matches
            // Microsoft.JSInterop.JSCallResultType so the base class's deserialiser
            // round-trips IJSObjectReference handle ids without further plumbing here.
            writer.WriteStartArray("jsInvokes");
            foreach (var invoke in jsInvokes)
            {
                writer.WriteStartObject();
                writer.WriteNumber("id", invoke.TaskId);
                writer.WriteString("identifier", invoke.Identifier);
                if (invoke.ArgsJson is not null)
                {
                    writer.WriteString("argsJson", invoke.ArgsJson);
                }

                writer.WriteNumber("resultType", invoke.ResultType);
                if (invoke.TargetInstanceId != 0)
                {
                    writer.WriteNumber("targetInstanceId", invoke.TargetInstanceId);
                }

                writer.WriteEndObject();
            }

            writer.WriteEndArray();
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

            if (download.Token is not null)
            {
                // WASM token-pull path: bytes stay .NET-side, JS pulls them via PullDownload
                // JSExport. Keeps the per-render JSON payload tight regardless of file size.
                writer.WriteString("token", download.Token);
            }
            else if (download.Bytes is not null)
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
            if (a == b)
            {
                continue;
            }

            if (a >= 'A' && a <= 'Z')
            {
                a = (char)(a + 32);
            }

            if (b >= 'A' && b <= 'Z')
            {
                b = (char)(b + 32);
            }

            if (a != b)
            {
                return false;
            }
        }

        return true;
    }

    internal static int IndexOfBodyOpenUtf8(ReadOnlySpan<byte> html)
    {
        // Scan UTF-8 bytes for "<body" followed by a tag boundary. Uses MemoryExtensions.IndexOf
        // for the initial '<' search (vectorized via Vector128/Vector256 on supported hardware).
        var bodyName = "body"u8;
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
        var firstUpper = first >= (byte)'a' && first <= (byte)'z' ? (byte)(first - 32) : first;
        var firstLower = first >= (byte)'A' && first <= (byte)'Z' ? (byte)(first + 32) : first;
        var end = source.Length - value.Length;
        var i = startIndex;
        while (i <= end)
        {
            var rel = firstUpper == firstLower
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
            if (x == y)
            {
                continue;
            }

            if (x >= (byte)'A' && x <= (byte)'Z')
            {
                x = (byte)(x + 32);
            }

            if (y >= (byte)'A' && y <= (byte)'Z')
            {
                y = (byte)(y + 32);
            }

            if (x != y)
            {
                return false;
            }
        }

        return true;
    }
}
