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
    ///     As <see cref="InjectRootAttr(string, string, bool)" />, and additionally stamps where the
    ///     client can ask about build status while the server is gone.
    /// </summary>
    /// <param name="html">The rendered page.</param>
    /// <param name="sessionId">The live session id, stamped as <c>data-rask-root</c>.</param>
    /// <param name="dev">Whether this host is in development.</param>
    /// <param name="devStatusUrl">
    ///     Where the client can ask <c>rask dev</c> whether the app is down because it is <em>broken</em>
    ///     or because it is <em>restarting</em> (#603). Stamped as <c>data-rask-dev-status</c> so the
    ///     client still has it after the server that sent it has gone away — which is the whole point,
    ///     since a failed rebuild is exactly when there is no server left to ask.
    /// </param>
    public static string InjectRootAttr(string html, string sessionId, bool dev, string? devStatusUrl)
    {
        var stamped = InjectRootAttr(html, sessionId, dev);

        // Never outside development, and never without the flag it sits beside: a production page must
        // not carry a localhost URL the browser would then poll.
        if (!dev || string.IsNullOrEmpty(devStatusUrl))
        {
            return stamped;
        }

        return InjectBodyAttr(stamped, "data-rask-dev-status", devStatusUrl);
    }

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

    /// <summary>
    ///     Stamps where the islands' Vite dev server is, as <c>data-rask-islands-dev</c>, so the island
    ///     runtime can load <c>@vite/client</c> and let each framework hot-replace its own modules.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Only the HMR CLIENT is signalled here; the island chunks themselves are already pointed
    ///         at the dev server by the manifest the build wrote. That split is deliberate — resolution
    ///         stays one mechanism with two tables, rather than two code paths in the runtime that
    ///         would then differ in dev and in production.
    ///     </para>
    ///     <para>
    ///         Development only, and never emitted otherwise: a production page carrying a localhost
    ///         URL would have every visitor's browser try to open a websocket to their own machine.
    ///     </para>
    /// </remarks>
    public static string InjectIslandsDevAttr(string html, bool dev, string? devServerUrl) =>
        dev ? InjectBodyAttr(html, "data-rask-islands-dev", devServerUrl) : html;

    /// <summary>
    ///     Loads the devtools host script into the page: a deferred <c>&lt;script&gt;</c> at the end of
    ///     <c>&lt;head&gt;</c>, marked <c>data-rask-managed</c>.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The host decides whether there is one: a Debug build that carries Rask.DevTools, running in
    ///         Development. Anywhere else <paramref name="hostScriptUrl" /> is null and the page is returned as
    ///         it was.
    ///     </para>
    ///     <para>
    ///         Written by the server rather than loaded by the client runtime, so <c>rask.js</c> and
    ///         <c>rask.wasm.js</c> — which every Release page loads — carry no code to load it. In the head
    ///         because the diff stream never addresses a node there: head changes arrive as a morph, and the morph
    ///         keeps a <c>data-rask-managed</c> node. A node the render never produced anywhere in the body would
    ///         be a node the diff's positional paths do not know about.
    ///     </para>
    /// </remarks>
    /// <param name="html">The rendered page.</param>
    /// <param name="hostScriptUrl">The devtools host script, or null when the page loads no devtools.</param>
    /// <param name="panelUrl">
    ///     The panel page the host script frames, written as <c>data-panel</c>. It carries the inspected session's
    ///     token, so it belongs to this response alone.
    /// </param>
    internal static string InjectDevToolsScript(string html, string? hostScriptUrl, string? panelUrl = null)
    {
        if (string.IsNullOrEmpty(hostScriptUrl))
        {
            return html;
        }

        var headClose = html.IndexOf("</head>", StringComparison.OrdinalIgnoreCase);
        if (headClose < 0)
        {
            return html;
        }

        var tag = "<script src=\"" + HtmlEncoder.Default.Encode(hostScriptUrl) + "\""
                  + (string.IsNullOrEmpty(panelUrl) ? "" : " data-panel=\"" + HtmlEncoder.Default.Encode(panelUrl) + "\"")
                  + " data-rask-managed defer></script>";
        return string.Concat(html.AsSpan(0, headClose), tag.AsSpan(), html.AsSpan(headClose));
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
        string? resume = null,
        DevErrorInfo? devError = null)
    {
        using var writer = new Utf8JsonWriter(output, DiffWriterOptions);
        WriteJson(writer, html, historyUrl, replace, auth, download, jsInvokes, resume, devError);
    }

    /// <summary>
    ///     Server live-path payload builder. Encodes <paramref name="html" /> to UTF-8 once,
    ///     locates the <c>&lt;body&gt;</c> / <c>&lt;/body&gt;</c> bounds on the byte span via
    ///     vectorized <see cref="MemoryExtensions.IndexOf{T}(System.ReadOnlySpan{T}, T)" /> (no
    ///     UTF-16 char-by-char scan), splices <c>data-rask-root="..."</c> on the opening tag,
    ///     and writes the JSON payload containing **only the body**. Replaces the prior
    ///     <see cref="InjectRootAttr(string, string, bool)" /> + <see cref="ExtractBody" /> +
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
        string? resume = null,
        DevErrorInfo? devError = null)
        => BuildPayloadUtf8Spliced(output, html.AsSpan(), sessionId, false,
            historyUrl, replace, auth, download, jsInvokes, resume, devError);

    // The session's full-HTML send: the page stays in its pooled buffer rather than becoming a string.
    internal static void BuildPayloadUtf8WithRoot(
        ArrayBufferWriter<byte> output,
        ReadOnlySpan<char> html,
        string sessionId,
        string? historyUrl,
        bool replace,
        AuthInstruction? auth,
        PendingDownload? download,
        IReadOnlyList<PendingJsInvoke>? jsInvokes,
        string? resume,
        DevErrorInfo? devError)
        => BuildPayloadUtf8Spliced(output, html, sessionId, false,
            historyUrl, replace, auth, download, jsInvokes, resume, devError);

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
        string? resume = null,
        DevErrorInfo? devError = null)
    {
        // Pass 1: the attribute-name symbol table (see BuildNameTable). A name can only reach the 3+
        // interning break-even across at least 3 attribute ops, so a smaller diff skips the pass.
        List<string>? internedNames = null;
        var nameIndex = ops.Count >= 3 ? BuildNameTable(ops, out internedNames) : null;

        using var rented = PayloadJsonWriter.Rent(output, DiffWriterOptions);
        var writer = rented.Writer;
        writer.WriteStartObject();
        writer.WriteString("kind", "diff");

        if (nameIndex is not null)
        {
            writer.WriteStartArray("names");
            foreach (var n in internedNames!)
            {
                writer.WriteStringValue(n);
            }

            writer.WriteEndArray();
        }

        writer.WriteStartArray("ops");
        foreach (var op in ops)
        {
            WriteOp(writer, op, nameIndex, newHtml);
        }

        writer.WriteEndArray();

        // Fire-and-forget IJSRuntime invokes (e.g. a scoped-JS OnRendered hook)
        // ride the diff payload the same way they ride the full-HTML payload, so a
        // component that calls js.InvokeVoidAsync on every render no longer forces the
        // whole page onto the full-HTML path. The client's diff branch drains these via
        // dispatchJsInvoke, identical to the full-HTML branch.
        WriteJsInvokesArray(writer, jsInvokes);

        // The diff frame stream never carries <head> content — user Head-asset contributions are
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

    // The interned-name table: every attribute name used 3+ times, mapped to its index in
    // `internedNames`; null when no name qualifies.
    //
    // Interned at 3+ times — break-even with the table overhead lands around there for typical
    // attribute names. Two occurrences of a short name like "class" cost more in the
    // table slot (`,"names":["class"]` ≈ 18 bytes) than they save in the two op refs
    // (~12 bytes saved). Three plus is comfortably net-positive for any name length.
    // Result: scenarios like AttributeBurstUpdate (100 ops sharing one name) drop
    // the duplicate name to a single integer per op (~1.2 KB saved); small diffs
    // pay no extra envelope.
    //
    // Larger diffs reuse per-thread scratch collections so an attribute-heavy steady-state render doesn't
    // reallocate the count map (and, in a burst, the index map + names list) every frame.
    private static Dictionary<string, int>? BuildNameTable(IReadOnlyList<EditOp> ops, out List<string>? internedNames)
    {
        Dictionary<string, int>? nameIndex = null;
        internedNames = null;

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

        return nameIndex;
    }

    private static void WriteOp(
        Utf8JsonWriter writer, EditOp op, Dictionary<string, int>? nameIndex, ReadOnlySpan<char> newHtml)
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
                writer.WriteStringValue(op.Value);
                break;
            case EditOpKind.RemoveAttribute:
                WriteInternedOrString(writer, op.Name, nameIndex);
                break;
            case EditOpKind.UpdateText:
                writer.WriteStringValue(op.Value);
                break;
            case EditOpKind.InsertSubtree:
                WriteFragment(writer, op, newHtml, emptyAsNull: true);
                writer.WriteNumberValue(op.Length);
                break;
            case EditOpKind.MorphSubtree:
                // [8, path, innerHtml] — the parent's new inner HTML, exactly like InsertSubtree
                // but with no trailing domCount.
                WriteFragment(writer, op, newHtml, emptyAsNull: false);
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

    // Prefer a verbatim Value (directly-constructed ops, incl. an emptied parent's "" fragment); otherwise
    // slice the fragment straight out of the render HTML by the op's deferred char range (the FrameDiffer
    // hot path), encoding to UTF-8 with no intermediate string. With neither, an insert writes null and a
    // morph writes the empty fragment.
    private static void WriteFragment(Utf8JsonWriter writer, EditOp op, ReadOnlySpan<char> newHtml, bool emptyAsNull)
    {
        if (op.Value is not null)
        {
            writer.WriteStringValue(op.Value);
        }
        else if (!newHtml.IsEmpty && op.HtmlStart >= 0 && op.HtmlEnd > op.HtmlStart
                 && op.HtmlEnd <= newHtml.Length)
        {
            writer.WriteStringValue(newHtml.Slice(op.HtmlStart, op.HtmlEnd - op.HtmlStart));
        }
        else if (emptyAsNull)
        {
            writer.WriteNullValue();
        }
        else
        {
            writer.WriteStringValue(string.Empty);
        }
    }

    private static void BuildPayloadUtf8Spliced(
        ArrayBufferWriter<byte> output,
        ReadOnlySpan<char> html,
        string sessionId,
        bool includeOnlyBody,
        string? historyUrl,
        bool replace,
        AuthInstruction? auth,
        PendingDownload? download,
        IReadOnlyList<PendingJsInvoke>? jsInvokes,
        string? resume = null,
        DevErrorInfo? devError = null)
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
        var sliceEndChar = bodyOpenChar < 0 ? -1 : SliceEnd(html, bodyOpenChar, includeOnlyBody);
        if (sliceEndChar < 0)
        {
            using var plain = new Utf8JsonWriter(output, DiffWriterOptions);
            WriteJson(plain, html, historyUrl, replace, auth, download, jsInvokes, resume, null);
            return;
        }

        var sliceStartChar = includeOnlyBody ? bodyOpenChar : 0;

        // The splice point is right after "<body". Encode three slices into one
        // pooled UTF-8 buffer:
        //   1. html[sliceStartChar .. bodyOpenChar + "<body".Length)   (head incl. "<body")
        //   2. " data-rask-root=\"{encodedSessionId}\""                (the injection)
        //   3. html[bodyOpenChar + "<body".Length .. sliceEndChar)     (tail)
        var headEndChar = bodyOpenChar + bodyOpenLen;
        var headSlice = html.Slice(sliceStartChar, headEndChar - sliceStartChar);
        var tailSlice = html.Slice(headEndChar, sliceEndChar - headEndChar);

        var encodedSessionId = HtmlEncoder.Default.Encode(sessionId);
        var totalBytes = Encoding.UTF8.GetByteCount(headSlice) + RootAttrPrefix.Length
                         + Encoding.UTF8.GetByteCount(encodedSessionId) + 1
                         + Encoding.UTF8.GetByteCount(tailSlice);

        var buffer = ArrayPool<byte>.Shared.Rent(totalBytes);
        try
        {
            var cursor = EncodeSplice(buffer.AsSpan(0, totalBytes), headSlice, encodedSessionId, tailSlice);
            var span = buffer.AsSpan(0, cursor);

            // Same relaxed encoder as the diff path — the WS payload is parsed by JSON.parse,
            // not embedded into HTML, so the default HTML-safe escaping inflates the "html"
            // field's `<` / `>` 5× for no security benefit. Shaves ~3-5 KB off a 10 KB page.
            using var rented = PayloadJsonWriter.Rent(output, DiffWriterOptions);
            WriteJsonUtf8Body(rented.Writer, span, historyUrl, replace, auth, download, jsInvokes, resume, devError);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    ///     <see cref="InjectRootAttr(string, string, bool)" /> straight to UTF-8: the page with its session id
    ///     on <c>&lt;body&gt;</c>, in a buffer rented from <see cref="ArrayPool{T}.Shared" />.
    /// </summary>
    /// <remarks>What a first response sends. The string in between was a second copy of the whole page.</remarks>
    internal static byte[] RentUtf8WithRootAttr(string html, string sessionId, out int length)
    {
        var bodyOpen = IndexOfBodyOpen(html);
        if (bodyOpen < 0)
        {
            var plain = ArrayPool<byte>.Shared.Rent(Encoding.UTF8.GetByteCount(html));
            length = Encoding.UTF8.GetBytes(html, plain);
            return plain;
        }

        var head = html.AsSpan(0, bodyOpen + "<body".Length);
        var tail = html.AsSpan(head.Length);
        var encodedSessionId = HtmlEncoder.Default.Encode(sessionId);
        var total = Encoding.UTF8.GetByteCount(head) + RootAttrPrefix.Length
                    + Encoding.UTF8.GetByteCount(encodedSessionId) + 1
                    + Encoding.UTF8.GetByteCount(tail);

        var buffer = ArrayPool<byte>.Shared.Rent(total);
        length = EncodeSplice(buffer.AsSpan(0, total), head, encodedSessionId, tail);
        return buffer;
    }

    private static ReadOnlySpan<byte> RootAttrPrefix => " data-rask-root=\""u8;

    // head + ` data-rask-root="{sessionId}"` + tail, UTF-8, into `span`; returns the bytes written.
    private static int EncodeSplice(
        Span<byte> span, ReadOnlySpan<char> headSlice, string encodedSessionId, ReadOnlySpan<char> tailSlice)
    {
        var cursor = Encoding.UTF8.GetBytes(headSlice, span);
        RootAttrPrefix.CopyTo(span[cursor..]);
        cursor += RootAttrPrefix.Length;
        cursor += Encoding.UTF8.GetBytes(encodedSessionId, span[cursor..]);
        span[cursor++] = (byte)'"';
        cursor += Encoding.UTF8.GetBytes(tailSlice, span[cursor..]);
        return cursor;
    }

    // Where the spliced payload ends: just past </body> when only the body ships, else the end of the page.
    // An unterminated body answers -1, since there is nothing sound to splice.
    private static int SliceEnd(ReadOnlySpan<char> html, int bodyOpenChar, bool includeOnlyBody)
    {
        if (!includeOnlyBody)
        {
            return html.Length;
        }

        var tagEndRel = html[bodyOpenChar..].IndexOf('>');
        if (tagEndRel < 0)
        {
            return -1;
        }

        var closeCharIdx = IndexOfIgnoreCase(html, "</body>", bodyOpenChar + tagEndRel + 1);
        return closeCharIdx < 0 ? -1 : closeCharIdx + "</body>".Length;
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
        ReadOnlySpan<char> html,
        string? historyUrl,
        bool replace,
        AuthInstruction? auth,
        PendingDownload? download,
        IReadOnlyList<PendingJsInvoke>? jsInvokes,
        string? resume = null,
        DevErrorInfo? devError = null)
    {
        writer.WriteStartObject();
        writer.WriteString("html", html);
        WriteJsonTail(writer, historyUrl, replace, auth, download, jsInvokes, resume, devError);
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

        if (PageVersion is { } version)
        {
            writer.WriteNumber(EventVersion.Name, version);
        }
    }

    /// <summary>
    ///     The number the payload being built carries as <c>v</c>, which the browser sends back with every event
    ///     it reads from that page — see <c>Component.StaleEvents</c>. Null for none.
    /// </summary>
    /// <remarks>
    ///     Set by the session around the one synchronous build it makes, rather than passed down: the builders
    ///     are public and their signatures are recorded, and a payload built by anything but a session — a test,
    ///     a prerender — carries no number, which is what null writes.
    /// </remarks>
    [ThreadStatic]
    private static int? PageVersion;

    /// <summary>Says which page the payload built next, on this thread, is. Undone by <see cref="EndPage" />.</summary>
    internal static void BeginPage(int? version) => PageVersion = version;

    /// <summary>The payload is built: the next one carries no number unless it is given one.</summary>
    internal static void EndPage() => PageVersion = null;

    /// <summary>
    ///     Writes the development error overlay record when a handler or async lifecycle hook threw.
    /// </summary>
    /// <remarks>
    ///     Rides inside the render payload for the same reason <see cref="WriteResume" /> does — the frame
    ///     stream is a contract, and an extra frame is observable in ways an extra field is not. It is also
    ///     exact here: the overlay only ever appears alongside the render that follows the fault.
    ///     <para>
    ///         Never written outside development: <see cref="DevErrorInfo.From" /> returns <c>null</c> there,
    ///         so a production payload cannot carry a stack trace even if a call site forgot to check.
    ///     </para>
    /// </remarks>
    private static void WriteDevError(Utf8JsonWriter writer, DevErrorInfo? devError)
    {
        if (devError is null)
        {
            return;
        }

        writer.WriteStartObject("devError");
        writer.WriteString("kind", devError.Kind);
        writer.WriteString("title", devError.Title);
        writer.WriteString("message", devError.Message);
        writer.WriteString("detail", devError.Detail);
        writer.WriteEndObject();
    }

    private static void WriteJsonUtf8Body(
        Utf8JsonWriter writer,
        ReadOnlySpan<byte> htmlUtf8,
        string? historyUrl,
        bool replace,
        AuthInstruction? auth,
        PendingDownload? download,
        IReadOnlyList<PendingJsInvoke>? jsInvokes,
        string? resume = null,
        DevErrorInfo? devError = null)
    {
        writer.WriteStartObject();
        writer.WriteString("html", htmlUtf8);
        WriteJsonTail(writer, historyUrl, replace, auth, download, jsInvokes, resume, devError);
    }

    private static void WriteJsonTail(
        Utf8JsonWriter writer,
        string? historyUrl,
        bool replace,
        AuthInstruction? auth,
        PendingDownload? download,
        IReadOnlyList<PendingJsInvoke>? jsInvokes,
        string? resume = null,
        DevErrorInfo? devError = null)
    {
        WriteResume(writer, resume);
        WriteDevError(writer, devError);
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

    /// <summary>
    ///     Inserts <c>name="value"</c>, the value HTML-encoded, right after the page's first <c>&lt;body</c>.
    ///     The one shape every per-response body stamp shares; no value, or a page with no body, leaves the
    ///     HTML as it was.
    /// </summary>
    private static string InjectBodyAttr(string html, string name, string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return html;
        }

        var i = IndexOfBodyOpen(html);
        if (i < 0)
        {
            return html;
        }

        var insertAt = i + "<body".Length;
        var attribute = " " + name + "=\"" + HtmlEncoder.Default.Encode(value) + "\"";
        return string.Concat(
            html.AsSpan(0, insertAt),
            attribute.AsSpan(),
            html.AsSpan(insertAt));
    }

    private static int IndexOfBodyOpen(ReadOnlySpan<char> html)
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

    private static int IndexOfIgnoreCase(ReadOnlySpan<char> source, string value, int startIndex)
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

    private static bool MatchesIgnoreCase(ReadOnlySpan<char> source, int sourceIndex, string value)
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
