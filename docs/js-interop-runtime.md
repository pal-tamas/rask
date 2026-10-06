# JS interop — IJSRuntime, typed APIs & refs

Calling JS from C#, the typed browser-API layer, element refs, and wrapping a third-party JS library.
The browser-side half is TypeScript — a `.js` sibling is [RASK055](diagnostics.md#rask055).

‹ Back to [JavaScript interop](js-interop.md)

## Calling JS from C# (`IJSRuntime`)

**Your component's own scoped `.ts` needs none of this** — its exports are typed private methods
(`await Width(_box)`), see [Calling your script from C#](js-interop.md#calling-your-script-from-c).
`IJSRuntime` is for everything else: a browser API, a library's global, another component's script.

Inject `IJSRuntime` through the **constructor** (not a property — a non-nullable settable
property would become a required chain step) and dispatch from a lifecycle hook or
event handler:

```csharp
public sealed partial class CodeSample : Component
{
    private readonly IJSRuntime _js;
    public CodeSample(IJSRuntime js) => _js = js;

    protected override async Task OnRendered() =>
        await _js.InvokeVoidAsync("hljs.highlightAll");
}
```

Nothing (no `el`) is passed automatically — pass what the function needs. For a return
value use `InvokeAsync<T>`. On WASM a non-primitive `T` must be rooted for the trimmer
(DAM annotation or a `JsonSerializerContext`).

A `sessionStorage` round-trip through the unified `IJSRuntime` — set, read, and remove, each a plain
`InvokeVoidAsync` / `InvokeAsync<string?>` against a built-in browser API, identical on both transports:

<!-- demo:js-interop-jsruntime -->

---

## Typed browser APIs

> For the **full map** of every wrapper (shared vs WASM-only, one-shot vs subscription), see the
> [Browser APIs overview](browser-apis.md). This section covers the shared set and the transport "why".

Rather than spelling out raw `IJSRuntime` identifiers (`"localStorage.getItem"`,
`"navigator.clipboard.writeText"`) and getting the JSON shape right by hand, call MDN's own surface from
[`Rask.Web`](web-apis.md) — `await LocalStorage.GetItem("theme")`, `await Navigator.Clipboard.WriteText("hi")`,
`await Crypto.RandomUUID()` —
or inject one of the built-in **typed wrappers** below through a component constructor. Each is a thin, awaitable layer over
the same unified `IJSRuntime`, so it behaves **identically on Server and WASM**. These are the
Web APIs that work on both transports; WASM-only PWA APIs (service worker, cache, manifest) are a
later step on the same pattern.

| Service | Wraps | Key members |
| --- | --- | --- |
| `IIndexedDb` | `IndexedDB` | `IsSupported`, `OpenStore(name)` → `IKeyValueStore` (`Set`/`Get`/`SetBytes`/`GetBytes`/`Delete`/`Keys`/`Clear`) — large async persistent storage, text or raw bytes |

The storage estimate is `await Navigator.Storage.Estimate()` in [`Rask.Web`](web-apis.md), with `Persist()` and
`Persisted()` beside it. Device tilt and motion are `Window` events there:
`await Window.OnDeviceOrientation(e => _angle = e.Alpha, every: 100.Milliseconds)`, throttled in the browser by `every:`.

```csharp
public sealed partial class Drafts(IIndexedDb db) : Component
{
    private async Task Save() => await (await db.OpenStore("drafts")).Set("note", "hi");

    protected override async Task OnFirstRender()
    {
        var note = await (await db.OpenStore("drafts")).Get("note");   // string?, null if absent
        var cookies = await Document.Cookie;                                       // "a=1; b=2", from Rask.Web
        var id = await Crypto.RandomUUID();                                        // string, from Rask.Web
    }
}
```

Call them from an **event handler or lifecycle hook** (not from `Render()`). Some APIs (WebAuthn, the
file system pickers, `crypto.subtle`) are **browser-gated** — they need a secure context (HTTPS or localhost) and the user's
permission; a denial or timeout surfaces as a `JSException` from the awaited task, so wrap those
calls in `try/catch`.

**User-activation and the transport — why one API is WASM-only.** Some browser APIs require
*transient* activation: they must run inside the live user-gesture task. On **WASM** an event
handler's interop call runs synchronously in that gesture's call stack, so it qualifies; on **Server**
the click is forwarded over the WebSocket and the interop call runs a round-trip later, after the
transient activation has expired. The practical effect:

- **Sharing** splits by *when* you fire it. The headless declarative **`Shareable`** (`Rask.Core`) attaches
  `data-rask-share` to your element and the shared client fires `navigator.share` **inside the click's own
  call stack**, so the activation is still live — it therefore works on **every** host, Server included. The
  imperative **`await Navigator.Share(…)`** ([`Rask.Web`](web-apis.md#what-only-webassembly-runs)) lets you
  share from *code* (a lifecycle hook, after an `await`), which needs the in-process transport to keep the
  activation — so it compiles only in a **WASM** app (on Server `navigator.share` would reject with
  "Must be handling a user gesture").
- **Fullscreen** and **Picture-in-Picture** need transient activation too. They are `await _stage.RequestFullscreen()`
  and `await _video.RequestPictureInPicture()` on an element ref in [`Rask.Web`](web-apis.md#on-an-element-ref),
  generated into WASM only. On Server, `Trigger.Fullscreen` and `Trigger.PictureInPicture` run them in the click.
- A custom install button's **`await _prompt.Prompt()`** (the kept `beforeinstallprompt` from
  `Window.OnBeforeInstallPrompt`, [`Rask.Web`](web-apis.md#where-a-browser-falls-short)) needs the click too, so it is
  **WASM-only**; on Server, `Trigger.Install` shows the same kept prompt in the click. Screen
  capture, `await Navigator.MediaDevices.GetDisplayMedia()`, is a [`Rask.Web`](web-apis.md#what-only-webassembly-runs)
  call generated into WASM only. See the [Mobile & PWA guide](pwa.md#device-capabilities-for-mobile).
- The **screen wake lock**, `await Navigator.WakeLock.Request(WakeLockType.Screen)` in
  [`Rask.Web`](web-apis.md#where-a-browser-falls-short), needs no transient activation, so it works on both hosts. The app badge is `await Navigator.SetAppBadge(3)` in [`Rask.Web`](web-apis.md).
- Everything else here (cookies, indexeddb) is unaffected by activation and
  behaves identically on both transports.

These are one-shot request/response calls. A *subscription* — `ISignaling`, `IWebRtc`, or a `Rask.Web` event such as
`Window.OnDeviceMotion` — hands back an `IAsyncDisposable` and the browser **pushes** each change to a C# handler.
Open it from a lifecycle hook and dispose of it on unmount. See
[the push pattern](browser-apis-sharing.md#subscriptions--the-push-pattern).

This is the rule for the whole surface: **shared APIs live in `Rask.Core.Browser`; APIs that can't
work on Server live in `Rask.Wasm.Browser`** (the home for upcoming PWA-only APIs too).

Under the hood: the wrappers' helpers (and `__raskEl`) live in `src/Rask.Core/Resources/rask-api.ts` and are
imported by both client runtimes at build time, so the two transports never drift. The types they deserialize are
rooted for the WASM trimmer by the framework, so they work in a `PublishTrimmed` app.

Runnable demos: the **Browser APIs** section of the showcase at
[rask.sh/docs](https://rask.sh/docs) — one demo per API, from
[`src/Rask.Site/Features/Browser/`](../src/Rask.Site/Features/Browser/).

---

## Element refs

A ref is a handle on one rendered element. **Type it to the element's MDN interface and it carries that
interface's DOM members, generated from MDN** — the same data the elements and their events come from:

```csharp
public sealed partial class RefDemo : Component
{
    private readonly ElementRef<HTMLDialogElement> _dialog = new();   // a field: the id is stable across renders
    private readonly ElementRef<HTMLInputElement> _name = new();
    private readonly ElementRef<HTMLVideoElement> _video = new();

    protected override Component? Render() =>
        Div[
            Input.Of<string>().Ref(_name),
            Video.Src("/intro.mp4").Ref(_video),
            Button.OnClick(Open)["Open"],
            Dialog.Ref(_dialog)[Button.OnClick(async () => await _dialog.Close())["Close"]]
        ];

    private async Task Open()
    {
        await _dialog.ShowModal();                     // MDN's showModal()
        var open = await _dialog.Open;                 // MDN's open, read from the live element
        await _name.Focus();                           // HTMLElement's focus(), which a dialog ref has too
        var box = await _name.GetBoundingClientRect(); // a DOMRect record
        await _video.SetCurrentTime(12);               // a write, where the render does not own the value
        await _video.Play();
    }
}
```

- **An operation is a method, an attribute an awaitable read**, named as MDN names them, with no `Async`
  suffix and no `IJSRuntime` to inject: the ref finds its element's session itself. Each member's doc comment
  gives its browser support and links to MDN.
- **Bases come with it.** `IElementRef<out T>` is covariant, so an `ElementRef<HTMLDialogElement>` has
  `HTMLElement`'s `Focus()` and `Element`'s `ScrollIntoView(…)`, `GetBoundingClientRect()` and `ScrollTop`.
  An untyped `ElementRef.New()` carries `Element`'s.
- **Options are MDN's dictionaries, as records; IDL enums are enums:**
  `ScrollIntoView(new ScrollIntoViewOptions { Behavior = ScrollBehavior.Smooth, Block = ScrollLogicalPosition.Nearest })`.
- **The render stays in charge.** There is no setter for what the render writes — `Open`, `Id`, a control's
  `Value` — and nothing that rewrites the tree, the attributes or the content (`innerHTML`, `append`,
  `setAttribute`). Drive those from state; keep the ref for what only the live node knows or does.
- **Only members whose values cross the wire are there.** A member that takes a kept `Rask.Web` object takes its
  handle (`await _video.SetSrcObject(stream)`); one that hands back a live `Node` is left out, and a ref still hands
  the element to your own TypeScript for those.
- **A typed ref on the wrong element throws** where it is put: an `ElementRef<HTMLVideoElement>` on a `Div`.
- **A member a browser only allows during a click** (`RequestFullscreen()`, `RequestPointerLock()`, an input's
  `ShowPicker()`) is generated into `Rask.Wasm` alone, where the handler
  runs in the click. On the Server host the call would reach the browser after the click has ended and be
  refused, so there it does not compile; `Trigger.Fullscreen` and `Trigger.PictureInPicture` run it in the click
  from markup instead.

A ref still serializes as `{"__raskRef__":"id"}`, and both clients revive it to the live DOM element, so it
passes to `IJSRuntime` or to your scoped TypeScript as the element itself:

```csharp
var width = await Width(_box);   // RefDemo.ts's `export function width(el: HTMLElement | null)`
```

Focus an input, measure a box, and open a dialog from C#, then measure the box again in a sibling `.ts`:

<!-- demo:js-interop-elementref -->

---

## Wrapping a third-party JS library

Everything above is enough to wrap a library that owns its own DOM — a chart, a code editor, a map. Two
questions come up every time: **what happens to the DOM the library builds**, and **what happens to the
`<style>` it injects**. Rask answers the second for you; the first is one rule.

### Describing the library to TypeScript

There is no `node_modules` in a Rask app, so a library's own typings are not there to install. Write a
`.d.ts` beside your component describing **only what you actually call** — any `.d.ts` in the project is
compiled alongside your scoped files, and a narrow declaration that is true is worth more than a
complete one copied from upstream that drifts, because the compiler believes either equally.

A hand-written `.d.ts` beside the scoped file is a worked example: about fifty lines
covering one constructor, two methods and three callbacks. Rask's own globals (`window.DotNet`,
`window.Rask`) are already declared for you and need no work.

### Give the library a leaf to own

Render the host element with **no children** and let the library fill it. That's the whole rule, and it
works because of how the diff addresses nodes: ops are computed from your C# render tree and applied by
positional path, so a node your components never render is a node the diff can never reach.

```csharp
private readonly ElementRef _host = ElementRef.New();   // a field — the id must be stable across renders

// A leaf: no children here, ever. The library owns everything inside it.
protected override Component? Render() => Div.Ref(_host).Class("chart");

// Mount the library in OnFirstRender, not OnMount — OnMount runs *before* the first render, so the element
// doesn't exist yet and the ref would resolve to null. OnFirstRender runs once, so it never mounts twice.
// Mount and Update are Chart.ts's own exports, called as typed methods.
protected override async Task OnFirstRender()
{
    await Mount(_host, DataAsJson());
}

// Fires only on a real prop change — push new data at the library instead of re-mounting it.
protected override async Task OnUpdated() => await Update(_host, DataAsJson());

// Sync and fire-and-forget — see the note below on why this must not be an awaited DisposeAsync.
protected override async Task OnUnmount() => _ = DestroyQuietly();
```

There is one exception to "the diff can't reach it", and it is not optional. Not every frame is a diff:
the first interactive frame after page load always ships the body in full, and a structural change can
too. The client applies a full frame by **morphing** the document, and a morph pairs each live child
against the rendered one — your host has live children where the render says none, so the morph clears
it. Skip that and the chart is deleted seconds after it draws. Tag the library's nodes; the reconciler
leaves marked nodes alone:

```js
// Right after the library builds its DOM. Mark what it created — never the host itself, which your
// component *does* render (marking that makes the morph treat it as missing and append a duplicate).
for (const child of host.children) child.setAttribute("data-rask-managed", "");
```

One more identity rule, because it bites stateful wrappers specifically: a component's identity is its
**(type, position)** among its parent's children. A sibling rendered as `cond ? node : null` shifts every
later child's position when it vanishes, so the wrapper gets matched against the wrong slot and rebuilt —
remounting the widget on an unrelated click. Prefer disabling to un-rendering a sibling above a
stateful component.

For events coming back the other way, give your scoped export a **callback parameter** —
`mount(host, data, onSelect: (id: string) => void)` is `Mount(_host, data, id => _selected = id)` in C#. It
re-renders the component after it runs and is released when the component unmounts; nothing to register.

A script you do not own can't reach an instance method — the JS shim
dispatches to **static** `[JSInvokable]`s by assembly and name. Hand JS a token at mount, keep a static
`ConcurrentDictionary<string, YourComponent>`, route on it, and unregister on unmount. Two things to get
right, because a `[JSInvokable]` is callable by *any* script on the page with *any* arguments:

- **Make the token unguessable** (`Guid.NewGuid().ToString("N")`). That dictionary is static, so on the
  Server host it is shared by every live session — with a sequential `int`, one visitor could drive
  another's widget by counting from 1. Holding the token is what proves ownership.
- **Unregister on unmount**, or the entry pins the component for the life of the process.

Keep the boundary to primitives and JSON strings and a trimmed WASM publish stays clean. Prefer callbacks
that take **one** argument (bundle extras into a record): the generated chain step only wraps arity-≤1
delegates for auto-re-render, so a two-arg callback silently leaves the caller reaching for
`StateHasChanged()`.

Finally, tear down from `OnUnmount` **without awaiting** the interop call. An `IAsyncDisposable`
component is awaited by the framework's dispose walk, and that walk also runs for a session whose socket
has already closed — where an interop call has nobody to answer it and never completes.

> A scoped `{Component}.css` **cannot** style the library's internals: scoping works by stamping
> `data-{scopeId}` on the elements your component renders, and the library's nodes never get it. Size the
> host in scoped CSS; let the library's own stylesheet handle the rest.

### What it injects into `<head>`

Rask treats `<head>` as **authoritative**: on every re-render the live-diff reconciler morphs the live
head back to what your components rendered, which keeps `<title>`/`<meta>`/scoped-CSS links correct. A
`<style>`/`<link>`/`<script>` that a **JS library injects into `<head>` at runtime** (a code editor's
theme colours, a charting library, a syntax highlighter, an analytics tag) isn't part of that render —
so **Rask preserves it for you automatically**. The reconciler watches `<head>` and tags anything a
library injects with `data-rask-managed` (the same marker it uses for its own scoped-asset tags), so it
survives every re-render with **no code on your side**. A code editor keeping its injected theme across
a re-render is the usual case.

The mechanism only preserves nodes injected *after* an initial render (the common case — libraries set up
once your component has mounted). If you need to keep something present at first paint, or want to be
explicit, mark it yourself — the reconciler never touches a head child carrying `data-rask-managed`:

```js
// You rarely need this — runtime-injected head nodes are preserved automatically. Use it only to opt a
// node out explicitly (e.g. one present before the app's first render).
styleEl.setAttribute("data-rask-managed", "");
```
