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

---

## Behaviour hooks (`data-rask-*`)

Some behaviour can be written neither as a render (which only writes attributes) nor as a handler (which runs a
round trip later, after the gesture is gone): showing a popover under the pointer, writing the clipboard,
keeping a caret in place. For those the runtime carries small generic hooks. An element asks for one by carrying
an attribute; every hook is a delegated listener on the document.
[Rask UI](ui-kit.md) is built on them, and they are just as usable from your own markup:
`Div.Data("rask-tooltip", "tip-1")[…]`.

They live in `src/Rask.Core/Resources/rask-hooks.ts` (one module per concern) beside the older ones in
`rask-dom.ts` — `data-rask-dismiss`, `data-rask-dismiss-after`, `data-rask-dismiss-hold`, `data-rask-focus-trap`,
`data-rask-popover-open`, `data-rask-dropzone`, `data-rask-shortcut`, `data-rask-contextmenu` — which are part
of the runtime itself.

### How the hooks load

**A page that carries none of these attributes does not download them.** The hooks in the tables below are a
script of their own, `rask-hooks.js` (37 kB, 12 kB gzipped), beside the runtime every page loads (`rask.js` on
the Server host, `rask.wasm.js` in a WebAssembly app). The runtime keeps only the list of attributes that ask
for a hook, and fetches the script the first time the page carries one — at most once per document:

| The attribute is… | What the network tab shows |
| --- | --- |
| nowhere on the page | `rask.js` only. No request for the hooks, however long the page lives. |
| in the page the **server rendered** | `rask.js` and `/rask/rask-hooks.js?v=…` side by side: the server writes the second `<script>` after the first in that response, so the hooks run straight after the runtime, as when they were one file. A dialog rendered open is a modal, and a remembered checkbox is restored, by the time the page has been read. |
| added later, by a render or an in-app navigation | one request for `rask-hooks.js`, when the attribute arrives. What the reader did to the page in between — a hover, a key, a press — is kept and handed to the hooks when they run: the tooltip under the pointer shows, the character typed into a one-time code moves on. Only what fires per pixel (a drag in flight, a chart's cursor) picks up at the next move. |
| in a **WebAssembly** app | `rask-hooks.js` from beside `rask.wasm.js`, requested when the runtime starts and finds one (a prerendered page usually has) or when a render adds one. |

Nothing is asked of you: there is no tag to write and nothing to register. Both scripts are served with the same
caching (one `?v=` names the pair on the Server host and both are immutable under it; a WebAssembly app serves
`rask-hooks.js` as it serves `rask.wasm.js`, and its service worker keeps it for offline use once fetched), from
your own origin, with the runtime's nonce when its `<script>` has one. Under a Content-Security-Policy the
hooks need what the runtime already needs — `script-src 'self'`, or the nonce — and no inline script.

Any element counts, whoever wrote it: your own markup, a `Raw` fragment, a node a script of yours inserted.
Besides the `data-rask-*` names, four of the platform's own ask for a hook, because a hook improves them
unasked: `popover`, `commandfor`, `aria-activedescendant` and `role="switch"` on a checkbox.

### Pointer-opened popovers

| Attribute | On | What the runtime does |
| --- | --- | --- |
| `data-rask-tooltip="<popover id>"` | the element wrapping a trigger and its `popover="manual"` bubble | Shows the bubble in the pointer's own task when it enters the wrapper and hides it when it leaves; shows it on keyboard focus (`:focus-visible`) and keeps it while that focus lasts; Escape hides it; a press on the trigger hides it until the pointer has left and come back. The element inside carrying `aria-expanded` has it kept true or false. Any element can be the trigger — the bubble reaches the top layer. A touch never hovers. |
| `data-rask-hover="<popover id>"` | the element wrapping a trigger and its panel | Opens the panel while the pointer is over the wrapper (trigger or panel) and closes it over neither — the pixels between them included. A press on the trigger's own `popovertarget` button leaves it open; Enter opens it the platform's way; focus alone does not. |
| `data-rask-hover-if="<selector>"` | the same element | The hover opens only while the element matches the selector — a rail item that opens its menu only while the sidebar is collapsed: `"input:checked ~ *"`. |
| `aria-expanded` on the trigger inside a `data-rask-tooltip` | an interactive tooltip (one whose bubble holds links or buttons) | Mirrored while the bubble shows. It also marks the tooltip as interactive: when focus drops to nothing (`blur()`, the window losing focus) the bubble stays, until a press outside it. A tooltip without it closes. |

### Popovers, dialogs and the page behind them

| Attribute | On | What the runtime does |
| --- | --- | --- |
| *(none)* | any `popover` / `popover="auto"` | When focus leaves an open one for anything but its own `popovertarget` button it closes; when a press outside closed it and focus is on `<body>`, focus goes to that button. A `popover="manual"` is left alone. |
| `data-rask-modal-open="true"` \| `"false"` | a `<dialog>` | `showModal()` / `close()` to match, when the attribute changes or the dialog arrives — a real modal with a `::backdrop`, where a rendered `open` is not. Removing the attribute closes it too. The dialog's own `close` and `cancel` events fire as usual. |
| `data-rask-modal="any"` \| `"press"` \| `"escape"` \| `"none"` | a `<dialog>` | How a reader dismisses it: Escape and a press outside, a press outside only, Escape only, neither. A press outside fires a cancelable `cancel`, then closes. With no value the dialog is the platform's. |
| `data-open` *(written)* | a dialog carrying either attribute above | Present while the dialog is shown, however it was opened or closed. |
| `command` / `commandfor` | a `<button>` | Where the engine has no invoker commands: `show-modal`, `close`, `request-close`, and the popover three. |
| `data-rask-toggle="<popover id>"` | any element that is not a `<button popovertarget>` | A click on it toggles that popover, and so do Enter and Space while it — or something inside it that is not a control of its own — has focus (Space does not scroll). A `popover="manual"` it opened is closed by Escape, by a press outside both, and by focus moving outside both; a press inside the popover leaves it open. A `popover="auto"` gets those from the platform, and a click on its toggle closes it rather than reopening it. Focus is yours: give the element a `tabindex` and a `role`. Do not also write `popovertarget`. |
| `aria-expanded` *(rewritten)* | an element that invokes a popover (`popovertarget`, `commandfor`, `data-rask-toggle`) and already carries `aria-expanded` | `"true"` while that popover shows and `"false"` when it does not, however it was opened or closed. Never added where the render did not write it; once written, a render no longer overwrites it. |
| `data-rask-lock` \| `data-rask-lock="scroll"` | a popover or dialog | While it is open `<html>` does not scroll, keeps its scrollbar gutter and — unless `"scroll"` — takes no pointer; the overlay itself stays usable. Counted, so two open overlays, one removed by a render, and a navigation all end unlocked. |

### Menus

| Attribute | On | What the runtime does |
| --- | --- | --- |
| `data-rask-menu-pointer` | a `[role=menu]` | The row under the pointer gets `data-active` at once and every other row of that menu loses it; none has it once the pointer leaves the menu. Move your own cursor from the row's `OnPointerEnter` and the render agrees with what is already on screen. |
| `data-rask-safe-area="<flyout id>"` | a row that opens a submenu | While the flyout is showing, the triangle between the pointer and the flyout's near edge belongs to the row, so the diagonal towards the flyout does not touch the rows it crosses. |

### Fields

| Attribute | On | What the runtime does |
| --- | --- | --- |
| `data-rask-copy="<id>"` | a button | Copies that element's value (or text) in the click itself, then carries `data-copied` for 2 s. |
| `data-rask-clear="<id>"` | a button | Empties that field, fires `input` and `change` on it, and focuses it. |
| `data-rask-focus="<id>"` | any element | Focuses that element after a click. |
| `data-rask-mask="(999) 999-9999"` | an `<input>` | Shapes what is typed: `9` a digit, `a` a letter, `*` either, anything else itself. The handler receives the shaped value; the caret stays where the reader was typing. |
| `data-rask-mask-money` \| `=".,2"` | an `<input>` | Groups an amount: decimal mark, thousands mark, decimals. |
| `role="switch"` | an `<input type="checkbox">` | Enter toggles it, as Space does. |
| `data-rask-big-step="<n>"` | an `<input type="range">` | Shift+Arrow and PageUp / PageDown move by `n`, firing `input` then `change`. |
| `data-rask-otp` \| `="alpha"` \| `="alphanumeric"` | the group around one-character inputs | The cells behave as one field: a character moves on, Backspace walks back, deleting closes up to the left, the arrows stop at the first empty cell, a paste fills from the first. Render the cells with no `value` and no handler, and bind ONE `<input type="hidden">` inside the group: the runtime keeps it equal to the code and fires `input` and `change` on it, and refills the cells when you change its value. |
| `data-rask-segments` + `data-rask-segment="month"` \| `"day"` \| `"year"` \| `"hour"` \| `"minute"` \| `"meridiem"` | the group around the small inputs of a typed date or time, and each input | The parts behave as one field: digits only, zero-padded; a part that can take no further digit moves on (3 is March, 9 is nine o'clock); ArrowLeft / ArrowRight walk the parts and stop at the ends; ArrowUp / ArrowDown step a part and wrap; Backspace empties a part and, on an empty one, steps back; `a` / `p` set the meridiem; a pasted date is shared out; a day the month does not have becomes its last; a one- or two-digit year is read within twenty years ahead. Render the parts in your locale's order with a `placeholder`, NO `value` and NO handler, and bind ONE `<input type="hidden">` inside the group: it carries `yyyy-mm-dd`, `HH:mm` (24-hour) or the two joined by `T` once every part is there, fires `input` and `change`, and refills the parts when you write it. A `readonly` part is left alone. |

### Keys and focus

| Attribute | On | What the runtime does |
| --- | --- | --- |
| `data-rask-contain-keys="Arrows Home End PageUp PageDown"` | a widget that handles those keys in its own handler | Cancels the browser's default for a listed key pressed inside it — the page does not scroll behind a calendar, Enter does not press a trigger — and nothing else: your handler still receives the key. Names are `KeyboardEvent.key`'s, plus `Space` and `Arrows` (all four). A key held with Ctrl, Alt or Meta is left alone, and so is one typed into a text field inside the widget. It is a list, not a rule per role, because no two widgets keep the same keys; render a different list when the widget's keys change (open / closed). |
| `data-rask-listbox-button` | a `button[role=combobox]` | The list `Enter ArrowUp ArrowDown`, while `aria-expanded` is not `true`: Enter does not press it and the arrows do not scroll the page. |
| `data-rask-roving` | a `[role=radiogroup]` of `[role=radio]` elements that are not native radios | ArrowDown / ArrowRight focus the next radio and ArrowUp / ArrowLeft the one before, wrapping at both ends; the radio focused is clicked, so your `OnClick` selects it; `tabindex` moves with it (`0` on the focused one, `-1` on the rest). Space clicks the focused one. Disabled radios are skipped. |
| `data-rask-focus-follows` + `data-rask-focus-target` | a container, and the one element in it that should hold focus | When a render moves the target mark — or replaces the element that carried it — while focus is ON that element, the new target is focused. Focus anywhere else is never taken. A render that leaves no target lets focus fall where the browser drops it. |
| `aria-activedescendant` | a `[role=combobox]` or `[role=listbox]` | When it changes, the option it names is scrolled into view inside its nearest scrolling ancestor, by the least movement; the page never scrolls. (A `[role=tree]` has its own rule, which also handles virtualized rows.) |
| `data-rask-press-keeps-focus` | any element | A mouse press on it or inside it does not move focus. |

### Gestures, capabilities and uploads

| Attribute | On | What the runtime does |
| --- | --- | --- |
| `data-rask-drag="x y"` \| `"x"` \| `"y"` | a surface holding ONE `<input type="hidden">` you bind | On a press the runtime captures the pointer and, on every move, writes where it is along each named axis — `0` to `1`, clamped, `0` at the left / top edge — into `--rask-drag-x` / `--rask-drag-y` on the surface, with no round trip: draw the thumb from those. The hidden field carries the same numbers (`"0.25 0.5"`, or the one axis) and fires `input` at most once per animation frame while the pointer moves and `change` once on release, so `OnInput` / `OnChange` on it are the whole C# side. Write the field's value yourself (a key handled in C#) and the properties follow, except while the surface is held. Give the surface `touch-action: none`. |
| `data-rask-drag-inset="<px>"` | the same surface | The track is that much shorter at both ends, so a thumb centred on the value stays inside the surface. |
| `data-rask-requires="<global>"` | a control for an API the browser may lack (`EyeDropper`) | `hidden` is set where `window` has no property of that name and removed where it has, when the control arrives. Render it `hidden`. The name must be a plain identifier; nothing is evaluated. |
| `data-rask-plot="0 0.25 0.5 1"` with `data-rask-plot-area`, `data-rask-plot-row="<index>"`, `data-rask-plot-tooltip="<px>"` | a chart's root: each row's x from 0 to 1 across the area; the element whose box is the plot; everything that belongs to one row (render every row's cursor, point and summary once); an absolutely placed box | While the pointer is inside the area the root carries `data-active`, `--rask-plot-x` (the nearest row's x), `--rask-pointer-x` and `--rask-pointer-y` (px from the root's corner); every element of the row nearest in x carries `data-active` — the switch is at the midpoint between two rows; the tooltip carries `data-active` and `transform: translate(x, y)`, x beside the row and y beside the pointer, each `<px>` away and flipped to the other side when the box would leave the root. Outside, the marks come off. No round trip: style `[data-active]`. |
| `data-rask-measure` | an element holding ONE `<input type="hidden">` you bind | The field is kept at `"<width> <height>"` of the element's content box, in CSS px, with `input` and `change` when it arrives and whenever it changes (a `ResizeObserver`, so at most once a frame) — what a chart needs to redraw at its real size. |
| `data-rask-loading` around an `<input type="file">` with `OnFiles` | the dropzone | From the moment files are chosen until the handler that receives them has rendered, the element carries `data-loading`, `--rask-progress` (a whole percentage, `12%`) and `--rask-progress-as-string` (`'12%'`, for `content:`). On the Server host the percentage is the upload request's own progress. In a WebAssembly app nothing is sent: it is how much of the files your handler has read through `OpenReadStream`, and stays `0%` for a handler that never opens them. `data-rask-loading="off"` opts out. |

### Toasts

| Attribute | On | What the runtime does |
| --- | --- | --- |
| `data-rask-dismiss-hold="pointer"` | an element with `data-rask-dismiss-after` | Only the pointer holds its countdown; focus inside it does not. |
| `data-rask-dismiss-scope` | an ancestor of several | The pointer anywhere over it holds every countdown inside, and they run on from where they stopped. |
| `data-rask-stack` | the element whose children are stacked | Each child gets, in its style, `--rask-stack-index` (0 at the front, the last child), `--rask-stack-height` (its own natural height), `--rask-stack-offset` (the natural heights in front of it) and `--rask-stack-front` (the front child's natural height), measured again when a child joins or leaves and when the window is resized — what a stylesheet needs to cut every card to the front one's height and glide the stack open. While it measures, the stack carries `data-rask-measuring`: write the rule that cuts a card as `[data-rask-stack]:not([data-rask-measuring]) …`, or the hook is handed the cut height back. |

### State the reader owns

| Attribute | On | What the runtime does |
| --- | --- | --- |
| `data-rask-persist="<key>"` | a checkbox | Checked as the reader last left it, from `localStorage[key]` (`"true"` / `"false"`), and stored on every change; a render no longer resets it. The runtime restores it when it loads — in a WebAssembly app that is after boot, so a page that must not flash restores the same key from a script of its own in `<head>`. |
| `data-rask-uncheck-on-navigate` | a checkbox | Unchecked (with a `change` event) when the app navigates to another path. |

### Carousels

| Attribute | On | What the runtime does |
| --- | --- | --- |
| `data-rask-carousel` | the root | On a scroll of its `[data-rask-carousel-track]` and on a resize: `data-at-start` / `data-at-end` on the root and on every `[data-direction]` wrapper, `disabled` on each wrapper's button, `data-selected` on the slide nearest the start and on its button in `[data-rask-carousel-indicators]` (with `aria-current="true"`). A press on a `[data-direction="next" \| "previous"]` button scrolls a slide; on an indicator, to that slide. |
| `data-advance="page"`, `data-wrap="rewind"`, `data-scroll="instant"` | the root | Move by the slides in view; go back to the first from the end; do not animate. |
| `data-autoplay="<ms>"` | the root | Advances on that interval and rewinds at the end; stops under the pointer and never starts under `prefers-reduced-motion`. |
| `data-rask-carousel-controls` + `data-name` | an element outside the root | Its wrappers and indicators drive the root with the same `data-name`. |

**A hook that writes an attribute holds it against the morph** (`rask-owned.ts`): `data-open`, `data-copied`, a tooltip's
`aria-expanded` and a carousel's `disabled` are not what the page rendered, and a re-render neither strips them
nor puts the rendered value back over them.
