# Rask.Web

**Every web API the browser ships, in C#, generated from MDN.** MDN's globals and interfaces, with MDN's names:

```csharp
using Rask.Web;

await Navigator.Clipboard.WriteText("hi");
var dark = await Window.MatchMedia("(prefers-color-scheme: dark)").Matches;
await LocalStorage.SetItem("theme", "dark");

await using var mql = await Window.MatchMedia("(min-width: 800px)");   // kept: a handle, disposed with the scope
var wide = await mql.Matches;
```

- **One round trip per `await`.** `Navigator.Clipboard.WriteText(…)` records the path and runs it in the browser
  once; nothing crosses the wire until you await.
- **An object you keep is a handle.** Awaiting a proxy (`await Window.MatchMedia(q)`), or a member whose promise
  resolves to an object, keeps that object in the browser until you dispose of it.
- **MDN's names, no `Async` suffix,** and each member's doc comment gives its browser support and links to MDN and the
  spec. `IsSupported` asks the browser whether it has one: `await Navigator.Clipboard.IsSupported`.
- **Both hosts.** On the server host each chain runs over the page's socket; in WebAssembly, in-process. Call it from
  an event handler or `OnRendered`, where the page is live.
- **The DOM stays Rask's.** Nothing that returns or rewrites DOM nodes is generated; use an element ref for an
  element's own members.

Part of [Rask](https://rask.sh). Docs: [browser APIs](https://rask.sh/docs/guides/browser-apis).
