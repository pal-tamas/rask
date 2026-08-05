using System.Buffers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Rask.Core.Authentication;
using Rask.Core.Routing;

namespace Rask.Core.Live;

public static class LivePayload
{
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

    // Per-thread scratch for the attribute-name symbol table (see BuildPayloadUtf8Diff). The diff
    // build is synchronous and non-reentrant, so a single attribute-heavy session reuses these
    // across renders instead of reallocating the count map every frame; concurrent sessions on
    // other threads get their own copies. Bounded by the largest attribute-name set a thread sees.
    [ThreadStatic] private static Dictionary<string, int>? _nameCountScratch;
    [ThreadStatic] private static Dictionary<string, int>? _nameIndexScratch;
    [ThreadStatic] private static List<string>? _internedNamesScratch;

    /// <summary>
    ///     The dev-only "an apply landed and every session has repainted" control frame. A fixed
    ///     literal — like the session-unknown payload — so it needs no reflection-based
    ///     serialization and can be sent without allocating.
    ///     <para>
    ///         The client branches on this exact text; <c>HotReloadMessageTests</c> and the
    ///         <c>rask.js</c> Node fixture both assert against this same constant so the two halves
    ///         cannot drift.
    ///     </para>
    /// </summary>
    internal const string HotReloadAppliedJson = """{"type":"hotReload","status":"applied"}""";

    internal static readonly byte[] HotReloadAppliedFrame = Encoding.UTF8.GetBytes(HotReloadAppliedJson);

    /// <summary>
    ///     The "this server is going away — reconnect somewhere else" control frame, broadcast to every
    ///     connected session at the top of a graceful shutdown. A fixed literal for the same reasons as
    ///     <see cref="HotReloadAppliedJson" />: no reflection-based serialization, no allocation.
    ///     <para>
    ///         Unlike the hot-reload frame this is <b>not</b> dev-gated. A production redeploy is exactly
    ///         when it matters: it is what lets the client say "Updating…" and come back where it was,
    ///         instead of reading the new process's <c>session/unknown</c> reply as an idle timeout and
    ///         showing "Your session timed out".
    ///     </para>
    ///     <para>
    ///         The client branches on this exact text; <c>ShutdownDrainTests</c> and the
    ///         <c>rask.js</c> source contract both assert against this same constant so the two halves
    ///         cannot drift.
    ///     </para>
    /// </summary>
    internal const string ServerShutdownJson = """{"type":"shutdown","status":"draining"}""";

    internal static readonly byte[] ServerShutdownFrame = Encoding.UTF8.GetBytes(ServerShutdownJson);

    /// <summary>
    ///     Stamps the session id onto <c>&lt;body&gt;</c> as <c>data-rask-root</c>, and in development
    ///     also <c>data-rask-dev</c> — the flag the client requires before it will act on any dev-only
    ///     frame. Production HTML never carries it, so those branches are unreachable there even if a
    ///     frame somehow arrived.
    /// </summary>
    public static string InjectRootAttr(string html, string sessionId, bool dev = false)
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
            sb.EnsureCapacity(html.Length + encoded.Length + 48);
            sb.Append(html, 0, insertAt);
            sb.Append(" data-rask-root=\"").Append(encoded).Append('"');
            if (dev)
            {
                sb.Append(" data-rask-dev");
            }

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
        AuthInstruction? auth = null,
        PendingDownload? download = null,
        IReadOnlyList<PendingJsInvoke>? jsInvokes = null)
    {
        // Used by the WASM host where the payload is handed to JS interop as a UTF-16 string.
        // The server host calls BuildPayloadUtf8 instead to skip the UTF-16 round-trip.
        var bytes = BuildPayloadUtf8(html, historyUrl, replace, auth, download, jsInvokes);
        return Encoding.UTF8.GetString(bytes);
    }

    public static byte[] BuildPayloadUtf8(
        string html,
        string? historyUrl,
        bool replace,
        AuthInstruction? auth = null,
        PendingDownload? download = null,
        IReadOnlyList<PendingJsInvoke>? jsInvokes = null)
    {
        var buffer = new ArrayBufferWriter<byte>(4096);
        BuildPayloadUtf8(buffer, html, historyUrl, replace, auth, download, jsInvokes);
        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>
    ///     Pooled-writer overload of
    ///     <see
    ///         cref="BuildPayloadUtf8(string,string,bool,AuthInstruction,PendingDownload,IReadOnlyList{PendingJsInvoke})" />
    ///     .
    ///     Writes the JSON payload into the caller-supplied buffer; callers reuse the writer
    ///     across frames (Clear / ResetWrittenCount) to avoid the per-frame 4 KiB allocation.
    /// </summary>
    public static void BuildPayloadUtf8(
        ArrayBufferWriter<byte> output,
        string html,
        string? historyUrl,
        bool replace,
        AuthInstruction? auth = null,
        PendingDownload? download = null,
        IReadOnlyList<PendingJsInvoke>? jsInvokes = null,
        string? resume = null)
    {
        using var writer = new Utf8JsonWriter(output, DiffWriterOptions);
        WriteJson(writer, html, historyUrl, replace, auth, download, jsInvokes, resume);
    }

    /// <summary>
    ///     Server live-path payload builder. Encodes <paramref name="html" /> to UTF-8 once,
    ///     locates the <c>&lt;body&gt;</c> / <c>&lt;/body&gt;</c> bounds on the byte span via
    ///     vectorized <see cref="MemoryExtensions.IndexOf{T}(System.ReadOnlySpan{T}, T)" /> (no
    ///     UTF-16 char-by-char scan), splices <c>data-rask-root="..."</c> on the opening tag,
    ///     and writes the JSON payload containing **only the body**. Replaces the prior
    ///     <see cref="InjectRootAttr" /> + <see cref="ExtractBody" /> +
    ///     <see cref="BuildPayloadUtf8(string,string,bool,AuthInstruction,PendingDownload,IReadOnlyList{PendingJsInvoke})" />
    ///     chain in one pass.
    /// </summary>
    public static byte[] BuildPayloadUtf8WithBody(
        string html,
        string sessionId,
        string? historyUrl,
        bool replace,
        AuthInstruction? auth = null,
        PendingDownload? download = null,
        IReadOnlyList<PendingJsInvoke>? jsInvokes = null)
    {
        var output = new ArrayBufferWriter<byte>(4096);
        BuildPayloadUtf8WithBody(output, html, sessionId, historyUrl, replace, auth, download, jsInvokes);
        return output.WrittenSpan.ToArray();
    }

    /// <summary>
    ///     Pooled-writer overload of
    ///     <see cref="BuildPayloadUtf8WithBody(string,string,string,bool,AuthInstruction,PendingDownload,IReadOnlyList{PendingJsInvoke})" />.
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
        AuthInstruction? auth = null,
        PendingDownload? download = null,
        IReadOnlyList<PendingJsInvoke>? jsInvokes = null)
        => BuildPayloadUtf8Spliced(output, html, sessionId, true,
            historyUrl, replace, auth, download, jsInvokes);

    /// <summary>
    ///     WASM live-path payload builder. Same UTF-8 splice as
    ///     <see cref="BuildPayloadUtf8WithBody(string,string,string,bool,AuthInstruction,PendingDownload,IReadOnlyList{PendingJsInvoke})" />,
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
        AuthInstruction? auth = null,
        PendingDownload? download = null,
        IReadOnlyList<PendingJsInvoke>? jsInvokes = null)
    {
        var output = new ArrayBufferWriter<byte>(4096);
        BuildPayloadUtf8WithRoot(output, html, sessionId, historyUrl, replace, auth, download, jsInvokes);
        return output.WrittenSpan.ToArray();
    }

    /// <summary>
    ///     Pooled-writer overload of
    ///     <see cref="BuildPayloadUtf8WithRoot(string,string,string,bool,AuthInstruction,PendingDownload,IReadOnlyList{PendingJsInvoke})" />.
    /// </summary>
    public static void BuildPayloadUtf8WithRoot(
        ArrayBufferWriter<byte> output,
        string html,
        string sessionId,
        string? historyUrl,
        bool replace,
        AuthInstruction? auth = null,
        PendingDownload? download = null,
        IReadOnlyList<PendingJsInvoke>? jsInvokes = null,
        string? resume = null)
        => BuildPayloadUtf8Spliced(output, html, sessionId, false,
            historyUrl, replace, auth, download, jsInvokes, resume);

    /// <summary>
    ///     Diff-mode payload: writes <c>{ "kind": "diff", "ops": [...] }</c> directly
    ///     into <c>output</c>. Each op is a positional JSON array whose
    ///     shape is fixed per <see cref="EditOpKind" /> — the client dispatches on
    ///     <c>op[0]</c> (the kind) and reads the remaining slots by position:
    ///     <code>
    ///         SetAttribute      [k, path[], name, value]
    ///         RemoveAttribute   [k, path[], name]
    ///         UpdateText        [k, path[], value]
    ///         InsertSubtree     [k, path[], html, domCount]
    ///         RemoveSubtree     [k, path[], domCount]
    ///         MoveSubtree       [k, path[], sourceSlot]
    ///         PermutationBatch  [k, parentPath[], moves[]]   // moves = [dst0,src0,dst1,src1,…]
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

    public static void BuildPayloadUtf8Diff(
        ArrayBufferWriter<byte> output,
        IReadOnlyList<EditOp> ops,
        string? historyUrl = null,
        bool replace = false,
        IReadOnlyList<PendingJsInvoke>? jsInvokes = null,
        string? headHtml = null,
        ReadOnlySpan<char> newHtml = default,
        string? resume = null)
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
        List<string>? internedNames = null;

        // A name can only reach the 3+ interning break-even across at least 3 attribute ops, so a
        // diff with fewer than 3 ops can never intern — skip the whole symbol-table pass (its
        // allocation and two loops) for the common small update. Larger diffs reuse per-thread
        // scratch collections so an attribute-heavy steady-state render doesn't reallocate the
        // count map (and, in a burst, the index map + names list) every frame.
        if (ops.Count >= 3)
        {
            var nameCount = _nameCountScratch ??= new Dictionary<string, int>(StringComparer.Ordinal);
            nameCount.Clear();
            for (var i = 0; i < ops.Count; i++)
            {
                var op = ops[i];
                if (op.Name is null
                    || (op.Kind != EditOpKind.SetAttribute && op.Kind != EditOpKind.RemoveAttribute))
                {
                    continue;
                }

                nameCount.TryGetValue(op.Name, out var c);
                nameCount[op.Name] = c + 1;
            }

            foreach (var kv in nameCount)
            {
                if (kv.Value < 3)
                {
                    continue;
                }

                if (nameIndex is null)
                {
                    nameIndex = _nameIndexScratch ??= new Dictionary<string, int>(StringComparer.Ordinal);
                    internedNames = _internedNamesScratch ??= new List<string>();
                    nameIndex.Clear();
                    internedNames.Clear();
                }

                nameIndex[kv.Key] = internedNames!.Count;
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
                    if (op.Value is null)
                    {
                        writer.WriteNullValue();
                    }
                    else
                    {
                        writer.WriteStringValue(op.Value);
                    }

                    break;
                case EditOpKind.RemoveAttribute:
                    WriteInternedOrString(writer, op.Name, nameIndex);
                    break;
                case EditOpKind.UpdateText:
                    if (op.Value is null)
                    {
                        writer.WriteNullValue();
                    }
                    else
                    {
                        writer.WriteStringValue(op.Value);
                    }

                    break;
                case EditOpKind.InsertSubtree:
                    // Prefer a verbatim Value (directly-constructed ops); otherwise slice the
                    // fragment straight out of the render HTML by the op's deferred char range
                    // (the FrameDiffer hot path), encoding to UTF-8 with no intermediate string.
                    if (op.Value is not null)
                    {
                        writer.WriteStringValue(op.Value);
                    }
                    else if (!newHtml.IsEmpty && op.HtmlStart >= 0 && op.HtmlEnd > op.HtmlStart
                             && op.HtmlEnd <= newHtml.Length)
                    {
                        writer.WriteStringValue(newHtml.Slice(op.HtmlStart, op.HtmlEnd - op.HtmlStart));
                    }
                    else
                    {
                        writer.WriteNullValue();
                    }

                    writer.WriteNumberValue(op.Length);
                    break;
                case EditOpKind.MorphSubtree:
                    // [8, path, innerHtml] — the parent's new inner HTML. Prefer a verbatim Value
                    // (directly-constructed ops, incl. the emptied-parent "" fragment); otherwise slice
                    // it out of the render HTML by the op's deferred char range (the FrameDiffer hot
                    // path), exactly like InsertSubtree but with no trailing domCount.
                    if (op.Value is not null)
                    {
                        writer.WriteStringValue(op.Value);
                    }
                    else if (!newHtml.IsEmpty && op.HtmlStart >= 0 && op.HtmlEnd > op.HtmlStart
                             && op.HtmlEnd <= newHtml.Length)
                    {
                        writer.WriteStringValue(newHtml.Slice(op.HtmlStart, op.HtmlEnd - op.HtmlStart));
                    }
                    else
                    {
                        writer.WriteStringValue(string.Empty);
                    }

                    break;
                case EditOpKind.RemoveSubtree:
                case EditOpKind.MoveSubtree:
                    writer.WriteNumberValue(op.Length);
                    break;
                case EditOpKind.PermutationBatch:
                    writer.WriteStartArray();
                    if (op.Moves is { } moves)
                    {
                        foreach (var m in moves)
                        {
                            writer.WriteNumberValue(m);
                        }
                    }

                    writer.WriteEndArray();
                    break;
            }

            writer.WriteEndArray();
        }

        writer.WriteEndArray();

        // Fire-and-forget IJSRuntime invokes (e.g. a scoped-JS OnRenderedAsync hook)
        // ride the diff payload the same way they ride the full-HTML payload, so a
        // component that calls js.InvokeVoidAsync on every render no longer forces the
        // whole page onto the full-HTML path. The client's diff branch drains these via
        // dispatchJsInvoke, identical to the full-HTML branch.
        WriteJsInvokesArray(writer, jsInvokes);

        // The diff frame stream never carries <head> content — user Head contributions are
        // collected and spliced post-render (see HeadAssetRegistry), so a title/asset change
        // produces zero ops. When the head changed the session attaches the new
        // <head>...</head> element here; the client morphs it into document.head alongside
        // applying the body ops, instead of falling back to a whole-document payload.
        if (headHtml is not null)
        {
            writer.WriteString("head", headHtml);
        }

        if (historyUrl is not null)
        {
            writer.WriteStartObject("history");
            writer.WriteString("action", replace ? "replace" : "push");
            writer.WriteString("url", historyUrl);
            writer.WriteEndObject();
        }

        WriteResume(writer, resume);

        writer.WriteEndObject();
    }

    private static void BuildPayloadUtf8Spliced(
        ArrayBufferWriter<byte> output,
        string html,
        string sessionId,
        bool includeOnlyBody,
        string? historyUrl,
        bool replace,
        AuthInstruction? auth,
        PendingDownload? download,
        IReadOnlyList<PendingJsInvoke>? jsInvokes,
        string? resume = null)
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
            BuildPayloadUtf8(output, html, historyUrl, replace, auth, download, jsInvokes, resume);
            return;
        }

        var sliceStartChar = includeOnlyBody ? bodyOpenChar : 0;
        int sliceEndChar;
        if (includeOnlyBody)
        {
            var tagEndRel = html.AsSpan(bodyOpenChar).IndexOf('>');
            if (tagEndRel < 0)
            {
                BuildPayloadUtf8(output, html, historyUrl, replace, auth, download, jsInvokes, resume);
                return;
            }

            var afterOpenTagChar = bodyOpenChar + tagEndRel + 1;
            var closeCharIdx = IndexOfIgnoreCase(html, "</body>", afterOpenTagChar);
            if (closeCharIdx < 0)
            {
                BuildPayloadUtf8(output, html, historyUrl, replace, auth, download, jsInvokes, resume);
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
            WriteJsonUtf8Body(writer, span[..cursor], historyUrl, replace, auth, download, jsInvokes, resume);
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
        AuthInstruction? auth,
        PendingDownload? download,
        IReadOnlyList<PendingJsInvoke>? jsInvokes,
        string? resume = null)
    {
        writer.WriteStartObject();
        writer.WriteString("html", html);
        WriteJsonTail(writer, historyUrl, replace, auth, download, jsInvokes, resume);
    }

    /// <summary>
    ///     Writes the session-resume record when one is due.
    /// </summary>
    /// <remarks>
    ///     It rides inside the render payload rather than arriving as its own frame, for the same reason
    ///     <c>history</c> and <c>auth</c> do. The frame stream is a contract: a <c>hello</c> with nothing
    ///     pending must emit no frame at all, and consumers reason about the last frame of a burst — so an
    ///     extra frame is observable in ways an extra field is not. It also happens to be exact: the record
    ///     only changes when the declared state or the route changes, and both always come with a render.
    /// </remarks>
    private static void WriteResume(Utf8JsonWriter writer, string? resume)
    {
        if (resume is not null)
        {
            writer.WriteString("resume", resume);
        }
    }

    private static void WriteJsonUtf8Body(
        Utf8JsonWriter writer,
        ReadOnlySpan<byte> htmlUtf8,
        string? historyUrl,
        bool replace,
        AuthInstruction? auth,
        PendingDownload? download,
        IReadOnlyList<PendingJsInvoke>? jsInvokes,
        string? resume = null)
    {
        writer.WriteStartObject();
        writer.WriteString("html", htmlUtf8);
        WriteJsonTail(writer, historyUrl, replace, auth, download, jsInvokes, resume);
    }

    private static void WriteJsonTail(
        Utf8JsonWriter writer,
        string? historyUrl,
        bool replace,
        AuthInstruction? auth,
        PendingDownload? download,
        IReadOnlyList<PendingJsInvoke>? jsInvokes,
        string? resume = null)
    {
        WriteResume(writer, resume);
        WriteJsInvokesArray(writer, jsInvokes);

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

    private static void WriteJsInvokesArray(Utf8JsonWriter writer, IReadOnlyList<PendingJsInvoke>? jsInvokes)
    {
        if (jsInvokes is not { Count: > 0 })
        {
            return;
        }

        // IJSRuntime.InvokeAsync<T> queue. Each entry resolves a dotted identifier
        // against `window` on the client (e.g. "sessionStorage.getItem"), invokes
        // it with the args (already JSON-encoded by the JSRuntime base class), and
        // ships the result back as { type: "jsResult", id, success, result|error }.
        // resultType drives how the client handles the return value (0=Default,
        // 1=JSVoidResult, 2=JSObjectReference, 3=JSStreamReference); matches
        // Microsoft.JSInterop.JSCallResultType so the base class's deserialiser
        // round-trips IJSObjectReference handle ids without further plumbing here.
        // Shared by the full-HTML tail (WriteJsonTail) and the diff envelope
        // (BuildPayloadUtf8Diff) so both wire shapes carry invokes identically.
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
