# Web APIs from MDN

**Every web API the browser ships, in C#, as MDN names it.** The `Rask.Web` package is generated from MDN's own
data — the same snapshot Rask's elements and events come from — so an MDN example ports line by line:

```csharp
using Rask.Web;

await Navigator.Clipboard.WriteText("hi");                           // navigator.clipboard.writeText("hi")
var dark = await Window.MatchMedia("(prefers-color-scheme: dark)").Matches;
await LocalStorage.SetItem("theme", "dark");
var page = await Document.VisibilityState;                           // an MDN enum: DocumentVisibilityState.Visible
```

The globals are the window's own: `Window` for its members (`MatchMedia`, `Fetch`, `Atob`), and one each for what
it holds — `Navigator`, `Document`, `Location`, `History`, `Screen`, `LocalStorage`, `SessionStorage`, `Performance`,
`Crypto`, `IndexedDB`, `Caches`, `CookieStore` and the rest. Every interface they reach is a C# type in
`Rask.Web.Types` (`Clipboard`, `MediaQueryList`, `Geolocation`), and MDN's option dictionaries and enums are records
and enums (`ShareData`, `DocumentVisibilityState`).

## One round trip per await

A chain is a path, recorded as you write it and run in the browser once when you `await`. Nothing crosses the wire
before that, so `Navigator.Clipboard` costs nothing on its own:

- **An object** is the next step of the path: `Navigator.Clipboard`, `Window.MatchMedia(q)`.
- **A value** is read at the end of it: `await Navigator.Language`, `await Window.MatchMedia(q).Matches`.
- **A write** is `Set{Name}`: `await Window.SetName("checkout")`. What the page renders has none: the page's `Head`
  sets the title, so there is `await Document.Title` but no `SetTitle`.
- **A method** runs there, and a promise is awaited there: `await Navigator.Clipboard.ReadText()`.

## Keeping an object

Awaiting an object keeps it: the browser holds it for you, and every member you call on it starts from it rather
than from the window. Dispose of it when you are done — `await using` does that for you:

```csharp
await using var wide = await Window.MatchMedia("(min-width: 800px)");   // kept: the same MediaQueryList each time
var matches = await wide.Matches;
```

A method whose promise resolves to an object (`Navigator.MediaDevices.GetUserMedia(…)`) keeps that object the same
way.

A method that returns an object at once is a step of the path like any other, so it runs when the path does — once
per await. `Window.MatchMedia(q).Matches` wants exactly that; a method you call for what it does, like
`Performance.Mark("start")`, wants awaiting on its own, which runs it once and keeps what it returns:
`await using var mark = await Performance.Mark("start");`.

## Constructors and static members

`new X(…)` is `X.Create(…)`, and the new object is kept: `await using var channel = await BroadcastChannel.Create("updates")`.
A static member is on the class, as in JavaScript: `await URL.CanParse(link)`, `await Notification.RequestPermission()`.

## Events and callbacks

An object's events are `On{Event}` — MDN's event name, like the element events — and subscribing returns a
subscription to dispose of. The handler runs in its component's order and re-renders it, and takes the event or
nothing, sync or async:

```csharp
_watch = await Window.MatchMedia("(min-width: 900px)").OnChange(e => _wide = e.Matches);   // MediaQueryListEvent
await using var online = await Window.OnOnline(() => _online = true);
```

The event is MDN's type — Core's `Event` where an element event uses the same one, else a type in `Rask.Web.Types`
(`MediaQueryListEvent`, `StorageEvent`) deriving from it — holding the fields that are values. A method that takes a
callback takes a C# handler for it, run and re-rendered the same way: `await Navigator.Geolocation.GetCurrentPosition(p => _where = p.Coords)`.

A handler has to belong to a component — a lambda written in one, or a method of it — since that is the component it
re-renders; the component unmounting drops it. Keep the subscription in a field and dispose of it in `OnUnmount` to
stop the browser listening too.

## Asking whether the browser has it

`IsSupported` asks the browser whether the object at the end of a path is there, instead of your guessing from its
user agent: `await Navigator.Clipboard.IsSupported`, `await Navigator.IsSupported`.

## Where to call it

From an event handler or `OnRendered`, where the page is live — on the server host each chain runs over the page's
socket, in WebAssembly in-process. A call made anywhere else throws, saying so. Browser-gated members (clipboard,
geolocation) can still be refused; that arrives as a `JSException` from the awaited call.

Every member's doc comment carries its browser support and links to MDN and the spec, straight from MDN's data.

<!-- demo:web-apis -->

## What it leaves to the rest of Rask

- **The DOM.** Nothing that returns or rewrites DOM nodes is generated: the render owns the page. Reach an element's
  own members through a [typed element ref](js-interop-runtime.md#element-refs) (`await _dialog.ShowModal()`).
- **What needs a live object as an argument** — a member that takes a `Node`, or a callback that hands one back — is
  not generated; the [typed browser API wrappers](browser-apis.md) cover those today.
- **`Rask.Web` is not imported for you yet.** Its globals share names with some of the wrappers' types, so a file that
  uses them says `using Rask.Web;`.
