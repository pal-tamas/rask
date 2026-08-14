# HTTP & files

Fetching JSON over HTTP and moving files in and out of the browser are plain .NET in Rask: a
dependency-injected `HttpClient`, the typed file-picker input, and the `Navigator` download bridge. The
*same* component code runs server-rendered over a WebSocket or client-side on WebAssembly — only the host
wiring differs. This guide walks the three, each with a live demo.

- [Fetching data with `HttpClient`](#fetching-data-with-httpclient) — register a DI'd client, fetch in `OnMountAsync`
- [Uploading files](#uploading-files) — a typed file picker and `RaskFile` metadata
- [Downloading files](#downloading-files) — stage bytes with `Navigator.Download`

For **server-side persistence** (EF Core + SQLite, `IDbContextFactory`, vertical slices), see the
[Data access](data-access.md) guide — this one is about data and file *transfer*.

---

## Fetching data with `HttpClient`

`HttpClient` is registered once in `Program.cs` and injected into components through the **constructor** —
no `[Inject]`, no service locator. Point its `BaseAddress` at the app's own origin so relative URLs
(`data/posts-1.json`) resolve against the static files the app serves itself; the demos below fetch a small
static JSON file, so the showcase stays self-contained and offline-safe.

The base address differs per host: on the Server it's the server's own origin; on WASM it's
`WasmHostBuilder.BaseAddress` — the app root, carrying any sub-path (a GitHub Pages deploy under
`/rask/`, say). Read it **lazily** inside the factory so it fires after the JS module imports:

<!-- demo:data-http-register -->

Inject the configured client and load in `OnMountAsync` — it runs once on first render, and the framework's
async lifecycle handler re-renders when the awaited task completes. `Component.CancellationToken` cancels on
unmount, so navigating away mid-fetch aborts the in-flight request instead of writing to a dead component:

<!-- demo:data-http-fetch -->

> **Same demo, two hosts.** Under `Rask.Example.Server` the request is a loopback call to the server's own
> static file; under `Rask.Example.Wasm` (and the GitHub Pages deploy) the browser fetches the same file
> from the AppBundle. The page code is identical — only the `BaseAddress` differs per host.

---

## Uploading files

`Input<string>().Type(InputType.File).Files(…)` wires a file picker to a typed handler. Each change event
hands the handler an `IReadOnlyList<RaskFile>`; `RaskFile` carries the metadata (name, size, content type,
last-modified) and `OpenReadStream` gives you a `Stream` for the bytes — over a multipart POST on the
Server, via JS chunked reads on WASM. The same component code runs unchanged on both hosts:

<!-- demo:data-upload -->

> A `RaskFile` is only valid while the handler is on the stack — read whatever you need (bytes, metadata)
> before returning. The mutating handler lives inside the component so its field updates re-render the right
> subtree.

---

## Downloading files

`Navigator.Download` stages bytes (or a stream) on the active session: on the Server they're served from
`/_rask/download/{sid}/{token}`; on WASM they're handed to JS as a base64 payload. The component code is the
same. It must be called from an **event handler** — outside that scope it throws, because there's no live
render round-trip to attach the download to. The handler can make other state changes too (here it bumps a
counter); both ship in the same render:

<!-- demo:data-download -->

---

See also: [Data access](data-access.md) for EF Core persistence, [Forms & validation](forms.md) for the
`Form<T>` pipeline and typed inputs, and [Lifecycle](lifecycle.md) for `OnMountAsync` and cancellation.
