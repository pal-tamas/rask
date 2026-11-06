# Live rendering & the diff codec

How a Rask app stays live after first paint, and how it ships the smallest
possible payload on every state change.

For what happens *before* that first paint — waiting for a page's async data, deciding whether the
page needs a live connection at all, and what status and cache headers the response carries — see
[Render modes](../render-modes.md). This document picks up once a session exists.

It expands the *live diff runtime* line of [`CLAUDE.md`](../../CLAUDE.md) with code
grounding. File references point at `src/Rask.Core/Live/`, `src/Rask.Server/`, and
`src/Rask.Wasm/`.

## Big picture: one component model, two transports

You write components once. The render walk, the frame stream, and the diff codec
all live in `Rask.Core` and are shared verbatim. Only the *transport* — how a
re-render reaches the browser DOM — differs by host:

```
                         Component tree (shared Rask.Core)
                                     │
                       HtmlSerializer.Serialize(...)
                                     │
              ┌──────────────────────┴──────────────────────┐
              │ HTML string                  RenderFrame stream │
              │ (StringBuilder)              (FrameWriter, parallel) │
              └──────────────────────┬──────────────────────┘
                                     │
                          FrameDiffer.Diff → List<EditOp>
                                     │
            ┌────────────────────────┴────────────────────────┐
            │ Server                              WASM         │
            │ LiveSession over a WebSocket        WasmLiveSession over JSImport/JSExport │
            │ (src/Rask.Server/LiveSession.cs)    (src/Rask.Wasm/WasmLiveSession.cs)     │
            └────────────────────────┬────────────────────────┘
                                     │
                  rask.js / rask.wasm.js → applyDiff / morph the DOM
```

- **Server** (`Rask.Server`): the runtime `<script>` opens a WebSocket. Inbound
  event-handler messages are dispatched in `LiveSession`; each render produces a
  payload that's pushed back down the socket.
- **WASM** (`Rask.Wasm`): there is no socket. `WasmLiveSession` is driven through
  `[JSImport]`/`[JSExport]` boundaries declared in `JSInterop.cs`. JS calls
  `[JSExport] Dispatch(byte[] json)` to deliver an event; .NET pushes the
  resulting payload back with the `[JSImport("applyRender")] ApplyRender(Span<byte>)`
  import, marshalled as a `MemoryView` — a zero-copy view over the session's write
  buffer that the JS side reads synchronously, so the result is *pushed*, not returned
  (see the comments in `JSInterop.cs`).

### The runtime `<script>` is auto-injected

`HtmlSerializer` injects the runtime as the **last child of `<body>`** — you never
write `RaskRuntimeScript()`. On serializing the `</body>` close it resolves the
host-registered `IRaskRuntimeScript` from DI and serializes its tag inline
(`HtmlSerializer.cs`, `CloseShellElement`):

```csharp
if (string.Equals(tagName, "body", StringComparison.Ordinal))
{
    live.BodyWritten = true;
    if (live.Services?.GetService<IRaskRuntimeScript>() is { } runtime)
    {
        AppendRuntimeScript(runtime, html);
    }
}
```

It is emitted **inline on every render** (not via a post-process sentinel) so its
bytes are stable across renders — the diff codec therefore never emits ops for it.
On **WASM no `IRaskRuntimeScript` provider is registered** (the runtime boots from
the page shell / `main.js`), so nothing is injected there.

## On this page

- [The render walk & diff codec](live-rendering-codec.md) — the parallel HTML+frame walk, the edit-op codec, keyed reconciliation.
- [Cache, head & dispatch](live-rendering-runtime.md) — SessionRenderCache, head/query-nav, handler ordering, slow-connection.

## See also

- [`../diagnostics.md`](../diagnostics.md) — **RASK022** (warning) flags a keyless list
  item that would reconcile positionally; add a `.Key(…)` to get trusted keyed structural
  ops instead of a full-HTML morph.
- [`CLAUDE.md`](../../CLAUDE.md) — *Primitives & rules* (the authoritative summary).
- [`../authentication.md`](../authentication.md) — the auth handshake that forces full
  HTML (the `auth` out-of-band instruction the diff path gates out).
