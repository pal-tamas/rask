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
way, and so does an attribute that is such a promise — the service worker's `ready`, which is where a Web Push
subscription starts:

```csharp
await using var worker = await Navigator.ServiceWorker.Ready;   // settles once the page's worker is active
await using var subscription = await worker.PushManager.Subscribe(new()
{
    UserVisibleOnly = true,
    ApplicationServerKey = Base64Url.DecodeFromChars(vapidPublicKey),   // System.Buffers.Text
});
var json = await subscription.ToJSON();   // MDN's PushSubscriptionJSON: what /_rask/push/subscribe and Push.Subscribe keep
```

A method that returns an object at once is a step of the path like any other, so it runs when the path does — once
per await. `Window.MatchMedia(q).Matches` wants exactly that; a method you call for what it does, like
`Performance.Mark("start")`, wants awaiting on its own, which runs it once and keeps what it returns:
`await using var mark = await Performance.Mark("start");`.

A method that takes an object, and a writable attribute that holds one, take one you kept, which crosses as its handle —
or `null`, where MDN allows it. A path you have not awaited is refused with "await it first":

```csharp
await using var hello = await SpeechSynthesisUtterance.Create("Hello");
await hello.SetRate(1.2);
await SpeechSynthesis.Speak(hello);

await Navigator.MediaSession.SetMetadata(await MediaMetadata.Create(new() { Title = "Song" }));
await Navigator.MediaSession.SetMetadata(null);
```

An attribute that holds one of several kinds of object is the first MDN lists, as a call's answer is: a media element's
`srcObject` is a `MediaStream`, so `SetSrcObject(null)` names one overload.

## Constructors and static members

`new X(…)` is `X.Create(…)`, and the new object is kept: `await using var channel = await BroadcastChannel.Create("updates")`.
A static member is on the class, as in JavaScript: `await URL.CanParse(link)`, `await Notification.RequestPermission()`.
An event's class has its statics too, though the event itself is what a handler gets — iOS asks before it fires a
device's motion: `var state = await DeviceOrientationEvent.RequestPermission();` (WebAssembly: in the click).

## On an element ref

What an element's members hand over or answer with as a live object is on its typed
[element ref](js-interop-runtime.md#element-refs) once `Rask.Web` is imported, a chain from the element like any other:

```csharp
await using var pip = await _video.RequestPictureInPicture();     // PictureInPictureWindow — WebAssembly: in the click
await _video.SetSrcObject(stream);                                // a MediaStream you kept; SetSrcObject(null) clears it
await using var fade = await _box.Animate([new() { ["opacity"] = 0 }, new() { ["opacity"] = 1 }], 300);
await using var slide = await _box.Animate(frames, new KeyframeAnimationOptions { Duration = "300", Easing = "ease-out" });
var running = await _box.GetAnimations();                         // Animation[], each one kept
await using var camera = await _canvas.CaptureStream();
```

Keyframes are CSS property names to values, one map per keyframe, each value a string or a number as in JavaScript.
The options' `new()` has to name its type, since `Animate(frames, 300)` takes the duration alone too. Only calls and
writes are here: a read of the element's own state (its `classList`, `style`, `dataset`) is the render's, and
nothing that holds the page's nodes (an `HTMLCollection`) is generated. A ref whose element has left the page is an
error, never the window in its place.

An element the browser names — `document.fullscreenElement`, `activeElement`, `pictureInPictureElement` — cannot cross,
but whether it is yours can: it reads as an `ElementRef` equal to the ref you rendered it with, and `null` for none or
for one you gave no ref. A ref is its id, so `==` compares the element it names:

```csharp
var current = await Document.FullscreenElement;
bool mine = current == _stage;                                   // readonly ElementRef<HTMLDivElement> _stage = new();
```

## Bytes, your own types, lists and loose objects

**Bytes are a `byte[]`**, both ways — a `BufferSource`, an `ArrayBuffer`, a `DataView` or a `Uint8Array` alike. They
cross as base64 and arrive as a `Uint8Array`; a buffer the browser answers with comes back as a `byte[]`. A method that
fills the array it is handed and answers with it does the same:

```csharp
var hash = await Crypto.Subtle.Digest("SHA-256", bytes);
var salt = await Crypto.GetRandomValues(new byte[16]);            // the filled array
await device.TransferOut(1, payload);                             // a USBDevice
var value = await characteristic.ReadValue();                     // a Bluetooth DataView, as bytes
var push = await PushManager.Subscribe(new() { UserVisibleOnly = true, ApplicationServerKey = vapidKey });
```

A method that fills the bytes it is handed but answers with something else (`AnalyserNode.GetByteFrequencyData`) is
not generated: the bytes cross as a copy, so you would never see them filled.

**Where MDN says `any`, it is your own type.** An argument is a type parameter, written by the host's own JSON options
as any JS interop argument is; a result is a generic read, and so is an event's `any` field:

```csharp
await channel.PostMessage(new CartChanged(42));                   // BroadcastChannel.postMessage(any)
var order = await response.Json<Order>();                         // Response.json(): Promise<any>
var state = await History.State<CartState>();                     // history.state
await using var _ = await channel.OnMessage(e => _last = e.Data<CartChanged>());   // MessageEvent.data
```

Your type is kept whole in a trimmed WebAssembly app, as for `InvokeAsync<T>`; under full AOT give it a
`JsonSerializerContext`, as Blazor asks. A promise of anything that settles with what your callback returned (a lock
request's) is only waited on. Bytes are bytes there too — a `byte[]` you hand over as an `any`, alone or anywhere inside
your own type, arrives as a `Uint8Array`, and one the browser answers with comes back as a `byte[]`.

A dictionary the browser answers with that holds an `any` is generic on your type, which is how a stream is read:

```csharp
await using var writer = await port.Writable.GetWriter();
await writer.Write(new byte[] { 0x01, 0x02 });                     // WritableStreamDefaultWriter.write(any)

await using var reader = await port.Readable.GetReader();          // the default reader: ReadableStreamDefaultReader
var r = await reader.Read<byte[]>();                               // ReadableStreamReadResult<byte[]>
if (!r.Done) Use(r.Value);
await reader.ReleaseLock();                                        // or reader.Cancel()
```

`GetReader()` is the reader MDN lists first, the one you get without asking for another; the overload that asks for a
BYOB reader is not generated, since C# could not tell which object it answers with.

**A list of live objects is an array of kept objects**, each disposed of on its own; an empty slot (a gamepad not
connected) is `null`:

```csharp
var devices = await Navigator.Usb.GetDevices();                   // USBDevice[], each one kept
var ports = await Navigator.Serial.GetPorts();
var pads = await Navigator.GetGamepads();                         // Gamepad?[]
var files = await Window.ShowOpenFilePicker();                    // WebAssembly: in the click
```

**An argument MDN types only as `object`** takes the dictionary its page documents, from a small table in the generator
(`src/Rask.Dom.Tasks/WebObjectArgs.cs`); without an entry it is not generated:

```csharp
await using var status = await Navigator.Permissions.Query(new() { Name = "geolocation" });
var state = await status.State;                                   // PermissionState.Granted
```

**A union is one C# type where one type can stand for every case:**

- **A value or a list of it** is the list — written as the list, and a lone value read back is a list of one: an
  observer's `Threshold`, an ICE server's `Urls`, a notification's `Vibrate`.
- **A string or a number** is a `string`, and crosses as the number where it spells one: `"6159"` goes as `6159`,
  `"battery_service"` and `"0x180F"` as text. The number is the short form of what the text also says (a Bluetooth
  service UUID), and where the text is a name instead — an animation's `"auto"`, a performance mark's name — a number
  written as text would be taken for one. Read back, a number is its text.
- **A `record<K, V>`** is a `Dictionary<string, V>`: a file picker's `Accept`, `Headers.Create(…)`, `PushSubscriptionJSON.Keys`.
- **A boolean or a dictionary** is the dictionary, nullable: `null` is `false` (left out, not asked for) and an empty
  one is `true`, since any object is truthy in JS — a media request's `Video`/`Audio`. Read back, the browser's
  `true` is an empty dictionary and its `false` is `null`.
- **A media constraint** — MDN's `ConstrainBoolean`, `ConstrainDouble`, `ConstrainULong`, `ConstrainDOMString` — is
  its plain value, nullable: `bool?`, `double?`, `int?`, `string?`, which the browser takes as the ideal. Read back,
  one a page wrote as an object is its `ideal`, else its `exact`, a list is its first item, and a range alone is `null`.

```csharp
_stream = await Navigator.MediaDevices.GetUserMedia(new() { Video = new() });   // video: true; no audio
_stream = await Navigator.MediaDevices.GetUserMedia(new() { Video = new() { Width = 640, FacingMode = "user", Torch = true } });
```

The constraints with no single plain value — `Pan`/`Tilt`/`Zoom` (a boolean or a number), `EchoCancellation` (a
boolean or a string) and `PointsOfInterest` (points) — are not generated.

```csharp
var battery = await Navigator.Bluetooth.RequestDevice(new()      // WebAssembly: in the click
{
    Filters = [new() { Services = ["battery_service"] }], OptionalServices = ["battery_service"],
});
var files = await Window.ShowOpenFilePicker(new()
{
    Types = [new() { Description = "Text", Accept = new() { ["text/plain"] = [".txt"] } }],
});
```

**An object that is nothing but values is a record**, wherever the browser hands it over: in a callback (an
observer's entries), in a read-only list (`await pad.Buttons` is a `GamepadButton[]`, `await device.Configurations` a
`USBConfiguration[]`), or in an event (a `DeviceMotionEvent`'s `Acceleration`, an `RTCErrorEvent`'s `Error`). Its
fields are typed as MDN says. One that holds a live object too (an XR input source, with its spaces) stays live, since
its record would lose it.

## Events and callbacks

An object's events are `On{Event}` — MDN's event name, like the element events — and subscribing returns a
subscription to dispose of. The handler runs in its component's order and re-renders it, and takes the event or
nothing, sync or async:

```csharp
_watch = await Window.MatchMedia("(min-width: 900px)").OnChange(e => _wide = e.Matches);   // MediaQueryListEvent
await using var online = await Window.OnOnline(() => _online = true);
```

The event is MDN's type — Core's `Event` where an element event uses the same one, else a type in `Rask.Web.Types`
(`MediaQueryListEvent`, `StorageEvent`) deriving from it — holding the fields that are values. A field that is a live
object is kept for you, the same object the browser fired with:

```csharp
await using var _ = await Navigator.Usb.OnDisconnect(async e =>
{
    await using var device = e.Device;                            // which device left
    _left = await device.ProductName;
});
```

An event made to be acted on later — a `BeforeInstallPromptEvent`, for a PWA's own install button — is kept whole, and
its method runs on it when you call it, in a click (WebAssembly):

```csharp
_install = await Window.OnBeforeInstallPrompt(e => _deferred = e);
// … in the install button's click:
var answer = await _deferred.Prompt();                            // PromptResponseObject
var accepted = answer.UserChoice == AppBannerPromptOutcome.Accepted;
```

The spec moved the outcome from the old `userChoice` promise onto what `prompt()` answers with, so it is read there.

**A hot event is throttled on the subscription.** A sensor fires ~60 times a second, and each one would cross to C#
and re-render; `every:` coalesces them in the browser before they cross. The first event goes at once, then at most one
per interval — the latest, so the last reading always arrives (the ones between are dropped before any payload is
built). Without `every` each event crosses, as before; disposing of the subscription clears its timer.

```csharp
_tilt = await Window.OnDeviceOrientation(e => _angle = e.Alpha, every: 100.Milliseconds);
_moved = await Window.OnDeviceMotion(e => _accel = e.Acceleration, every: 100.Milliseconds);
```

Each one is a handle the browser holds until you dispose of it, or until the handler's component unmounts — so dispose
of what you do not keep. An event that only ever fires on the object it names (a HID device's `inputreport`, many a
second) leaves that field out: you are holding it already. A method or constructor
that takes a callback takes a C# handler for it, run and re-rendered the same way, and an element it takes is your
`ElementRef`:

```csharp
await Navigator.Geolocation.GetCurrentPosition(p => _where = p.Coords);

await using var io = await IntersectionObserver.Create(entries =>
    _visible = entries.Where(e => e.IsIntersecting).Select(e => e.IntersectionRatio).ToArray());
await io.Observe(_card);                                          // readonly ElementRef _card = ElementRef.New();

await Navigator.Locks.Request("sync", async lk => await Sync());  // the lock is held until Sync() returns
await Navigator.Locks.Request("sync", new() { IfAvailable = true }, lk => _free = lk is not null);   // null: held elsewhere
```

What a callback is handed is data, read when it ran: an `IntersectionObserverEntry`, a `ResizeObserverEntry`, a
`MutationRecord`, a `Lock` is a record under MDN's name, like `DOMRect`, its fields typed as MDN says (`e.IsIntersecting`
is a `bool`). A field that is a node (an entry's `target`) is left out. The handler takes the arguments it can use, in
order — an observer's entries, not the observer, which you already hold. A callback whose promise the browser waits on
(a lock's) is finished before the browser goes on; one whose result the browser reads (an `Observable` predicate) is not
generated.

A handler has to belong to a component — a lambda written in one, or a method of it — since that is the component it
re-renders; the component unmounting drops it. A handler the browser awaits is the exception: one written outside any
component (a hosted service's lock request) runs at once, re-renders nothing, and is dropped when the call that runs
it settles. Keep the subscription or the observer in a field and dispose of it in
`OnUnmount` to drop its handlers sooner; `Disconnect()` an observer to stop the browser watching.

## Asking whether the browser has it

`IsSupported` asks the browser whether the object at the end of a path is there, instead of your guessing from its
user agent: `await Navigator.Clipboard.IsSupported`, `await Navigator.IsSupported`. A class has one too, for the
guard before `Create` — `if (await EyeDropper.IsSupported)` asks `"EyeDropper" in window`. A method has none, since a
C# method has no members to ask with: a method on a global (`Window.ShowOpenFilePicker`) that this browser lacks
answers the call with a `JSException`.

A web API is generated once ONE browser engine ships it, so the device APIs only Chromium has are here too — WebUSB,
WebHID, Web Bluetooth, the Battery Status and Network Information APIs, `navigator.vibrate`, the EyeDropper and Idle
Detection. Their doc comment opens with **Chromium only.** (or Firefox, or Safari), and `IsSupported` is the guard:

```csharp
if (await Navigator.Usb.IsSupported)
{
    var device = await Navigator.Usb.RequestDevice(new() { Filters = [new() { VendorId = 0x2341 }] });
}
```

Elements and their attributes stay cross-engine: markup only gets what two engines ship.

What is on no standards track is left out, except a short list named one by one in `scripts/mdn/refresh.mjs`
(`NON_STANDARD`), each something a real app needs and nothing standard does: today `BeforeInstallPromptEvent`. Their
doc comments say **Non-standard.**

## Testing

`Fake()` stands in for a web object in a test, for the test's own flow, until disposed of: every chain that starts at
it is answered by the fake, and nothing reaches a browser.

```csharp
using var clipboard = Navigator.Clipboard.Fake();
clipboard.Returns(c => c.ReadText(), "pasted");

await page.Click("Paste");

Assert.Equal("writeText", clipboard.Calls.Single().Member);
```

A read or call nobody set up answers the type's default, a write is remembered for the next read, and an object kept
from a fake stays in it. `Raise("change", new MediaQueryListEvent { Matches = true })` fires an event at the handlers
subscribed to it, which run in their components as the browser's would. The globals fake the same way:
`using var storage = LocalStorage.Fake();`.

`Returns(l => l.IsSupported, false)` makes a fake absent, `Throws(s => s.Persist(), new JSException("denied"))` fails
a member as the browser refusing it would, and `CallsBack` runs the handler a call such as a lock request was handed,
the call settling when it has:

```csharp
using var locks = Navigator.Locks.Fake();
locks.CallsBack<Lock?>("request", (call, handler) => handler(new Lock { Name = (string)call.Args[0]! }));
```

## Where to call it

From an event handler or `OnRendered`, where the page is live — on the server host each chain runs over the page's
socket, in WebAssembly in-process. A call made anywhere else throws, saying so. (Rask's own services that call one
outside a handler — the browser SQLite host at boot, the culture cookie — name the page they run on themselves.) Browser-gated members (clipboard,
geolocation) can still be refused; that arrives as a `JSException` from the awaited call.

Every member's doc comment carries its browser support and links to MDN and the spec, straight from MDN's data, and
says so when it only exists on an HTTPS page (or localhost): MDN's `[SecureContext]`.

### What only WebAssembly runs

Two kinds of member are generated into `Rask.Wasm` alone, as extensions of the same types, so in a WebAssembly app
they read like any other and in a server app they do not compile:

- **A call the browser allows only during the user's click** — `Navigator.Share(…)`,
  `Notification.RequestPermission()`, `MediaDevices.GetDisplayMedia(…)`, `ScreenOrientation.Lock(…)`,
  `PaymentRequest.Show()`, `DeviceOrientationEvent.RequestPermission()`, and on element refs `RequestFullscreen()`,
  `RequestPointerLock()`, `ShowPicker()` and `RequestPictureInPicture()`.
  On the server host the click's frame crosses the socket first, the browser lets the
  click go, and the call would fail with `NotAllowedError`. The IDL does not mark these; the list is Rask's
  (`src/Rask.Dom.Tasks/WebHost.cs`).
- **A family driven every frame** — WebGL (and its extension objects), WebGPU and audio worklets, where a round trip
  per call is no way to draw.

```csharp
await Navigator.Share(new ShareData { Title = "Rask", Url = "https://rask.sh" });  // WebAssembly: runs in the click
                                                                                    // Server: does not compile
```

On the server host, share from markup instead: `Shareable` and the `Trigger.*` components run the call inside the
click itself.

<!-- demo:web-apis -->

## What it leaves to the rest of Rask

- **The DOM.** Nothing that returns or rewrites DOM nodes is generated: the render owns the page. Reach an element's
  own members through a [typed element ref](js-interop-runtime.md#element-refs) (`await _dialog.ShowModal()`).
- **What needs a live object the C# side cannot name** — a member that takes a `Document` or a text node, a callback
  that hands back a live object (an `IdleDeadline` to ask the time left of), or one whose result the browser reads — is
  not generated; the [typed browser API wrappers](browser-apis.md) cover those today.
- **`Rask.Web` is not imported for you yet.** Its globals share names with some of the wrappers' types, so a file that
  uses them says `using Rask.Web;`.
