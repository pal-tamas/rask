# Browser APIs

Rask ships **typed C# wrappers over the browser's Web APIs** — inject one through a component
constructor and call it, instead of hand-writing `IJSRuntime` identifiers and getting the JSON
shape right yourself. Each is a thin, awaitable layer over the same unified
[`IJSRuntime`](js-interop-runtime.md#calling-js-from-c-ijsruntime), so it works the same way whether your
app runs on the **Server** (WebSocket) or **WASM** (`JSImport`/`JSExport`) transport.

This page is the **map of the whole surface**. For an at-a-glance view of *where each API works*
(Web / PWA), see the
[**capability matrix**](browser-capabilities.md) — it links to a dedicated reference page per API
under [`docs/apis/`](apis/). For the deeper "why" — user activation, the transport seam, element
refs — see [JS interop → Typed browser APIs](js-interop-runtime.md#typed-browser-apis); for the mobile/PWA
angle see the [Mobile & PWA guide](pwa.md). Every wrapper has a runnable demo in the
[showcase](https://rask.sh/docs/), under **Browser APIs** — except the WASM-only tier
plus `IWakeLock` and `IWebPush`, which get their own pages under **PWA** because they need something the
Server transport can't give them. (The six activation-gated ones appear in both: as gesture components
under Browser APIs, and as injectable services under PWA.)

## Two homes, one rule

- **`Rask.Core.Browser`** — APIs that work on **every host** (Server + WASM). Registered by both.
- **`Rask.Wasm.Browser`** — APIs only the WASM host can run: they need the installed-PWA instance, a live
  document, a device chooser, or *transient* user activation — preserved only when the interop call runs
  inside the click's own call stack, which the Server's WebSocket round-trip loses.

> **The rule:** shared-everywhere APIs live in `Rask.Core.Browser`, WASM-only ones in
> `Rask.Wasm.Browser`. A host simply doesn't register a service it can't provide.

Sharing shows the split cleanly. The **declarative, headless** `Shareable` (Rask.Core) hands *your* markup
a `data-rask-share` attribute and the shared client fires `navigator.share` *inside the click gesture* — no
round-trip, so activation survives — so it works on **every** host, Server included. The **imperative**
`IShare` (Rask.Wasm.Browser) lets you share from code (a lifecycle hook, after an `await`), which only the
WASM host can do, so it lives in the WASM-only home.

Inject through the **constructor** (not a settable property — that would become a required chain
parameter) and call from an **event handler or lifecycle hook**, never from `Render()`:

```csharp
public sealed partial class ThemeToggle(IBrowserStorage storage, IMediaQuery media) : Component
{
    protected override async Task OnRenderedAsync(bool first)
    {
        if (!first) return;
        var saved = await storage.Local.GetAsync("theme");
        var dark = saved is null ? await media.PrefersDarkAsync() : saved == "dark";
        // …apply theme…
    }
}
```

Browser-gated APIs (clipboard, geolocation, notifications, fullscreen, crypto's secure context, …)
can fail — a denial/timeout/unsupported surfaces as a `JSException` from the awaited task, so gate on
the API's `IsSupported`/permission check and wrap calls in `try/catch`.

**Writing TypeScript rather than C#?** These wrappers reach the browser through modules that are
themselves ordinary TypeScript, and an SPA or meta framework imports them directly instead —
`import { getCurrentPosition } from './rask/browser/geolocation'`. One implementation, four front
ends: a quirk fixed for the C# caller is fixed for the TypeScript one in the same commit. See
[TypeScript front ends → Browser APIs](spa.md#browser-apis), and the third column of the
[capability matrix](browser-capabilities.md) for which APIs ship a module and which you should simply
call on the platform.

## On this page

- [The sharing model](browser-apis-sharing.md) — shared vs WASM-only wrappers, declarative vs imperative, the subscription push pattern.
- [Reference & live demos](browser-apis-reference.md) — every wrapper with a runnable demo.

## Notes

- **Secure context.** Clipboard, geolocation, notifications, push, `crypto.subtle`, and others require
  HTTPS or `localhost`.
- **Permission/support gating.** Many APIs expose `IsSupportedAsync()` and/or pair with `IPermissions`;
  check before triggering a prompt, and `try/catch` the call.
- **Trimming (WASM).** Types these APIs deserialize are registered in source-gen JSON contexts, and the
  push APIs' `[JSInvokable]` methods are `[DynamicDependency]`-rooted, so everything stays correct in a
  `PublishTrimmed` app.

See also: [JS interop](js-interop.md) · [Mobile & PWA](pwa.md).
