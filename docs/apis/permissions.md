# IPermissions

> Query a permission's state (`granted`/`denied`/`prompt`) before prompting.

- **Wraps:** Permissions API
- **MDN:** [Permissions API](https://developer.mozilla.org/en-US/docs/Web/API/Permissions_API)
- **Home:** `Rask.Core.Browser` (all hosts)
- **Shape:** one-shot
- **Availability:** Web/Server ✅ · PWA/WASM ✅ · Native ✅ ★
- **Native backend:** OS authorization status (iOS `CLLocationManager`/`UNUserNotificationCenter`/`AVCaptureDevice`) · `Activity.CheckSelfPermission` (Android)

## In the browser, support is patchy

`navigator.permissions.query` answers for different names in different engines. WebKit (Safari, and any
iOS WebView) answers only for `camera` and `microphone` — verified against `WKWebView`; `geolocation` and
`notifications` throw `NotSupportedError`, and the clipboard/persistent-storage names throw `TypeError`. An
unrecognised name faults the awaited task with a `JSException`, so gate accordingly — or catch and treat a
fault as "unknown".

## On Native it answers about whatever you're actually about to use

This is the wrapper's sharpest edge, and why it has a native backend. In the [native shell](../native.md),
`IGeolocation`, `INotifications` and `IClipboard` resolve to **native backends gated by the OS app
permission** — the `Info.plist`/manifest grant and the system prompt. The WebView's Permissions API cannot
see that: it describes the WebView's own grants, a different system from the one those backends use.

So on Native each name is answered from the gate the caller will actually meet, and all seven answer:

| `PermissionName` | iOS | Android |
|---|---|---|
| `Geolocation` | `CLLocationManager.AuthorizationStatus` | `CheckSelfPermission(ACCESS_FINE_LOCATION)` |
| `Notifications` | `UNUserNotificationCenter` settings | `AreNotificationsEnabled` + `POST_NOTIFICATIONS` (API 33+) |
| `Camera` / `Microphone` | **the WebView** — see below | **the WebView** — see below |
| `ClipboardRead` | `Prompt` — iOS 16+ can raise "Allow Paste?" | `Granted` — `ClipboardManager` needs no grant |
| `ClipboardWrite` | `Granted` — writing never prompts | `Granted` — `ClipboardManager` needs no grant |
| `PersistentStorage` | `Granted` — app storage is never evicted | `Granted` — app storage is never evicted |

> **`Camera`/`Microphone` deliberately stay with the WebView.** Nothing on a native head consumes the app's
> camera/mic grant — `IMediaDevices` is WASM-only, so capture goes through the WebView, which gates it on its
> *own* permission (`WKWebView`'s per-origin decision, Android's `WebChromeClient.OnPermissionRequest`) on
> top of the app's. Answering from `AVCaptureDevice`/`CheckSelfPermission` would report `Granted` for a
> capture the WebView is still going to gate. These are also the only two names WebKit answers well, so
> deferring is both the accurate and the well-supported choice.

> **Android reports `Prompt`, never `Denied`, for a permission it can request.** `Prompt` there means
> exactly *"not granted — asking is still worth a try"*, not a promise that a dialog will appear: a
> permanently-denied permission and one that was never requested look identical to `CheckSelfPermission`,
> and a permission missing from the manifest is refused with no dialog at all.
> `ShouldShowRequestPermissionRationale` doesn't close the gap either, being `false` both before the first
> ask and after a permanent denial. Claiming `Denied` instead would be wrong on first run, which is the
> common case — so the OS decides whether a dialog appears, and you should be ready for it not to.
> `Notifications` is the exception: it *can* report `Denied`, because `AreNotificationsEnabled` sees a user
> who switched notifications off. iOS has the real tri-state throughout and reports it.

## See also

- Source: [`IPermissions.cs`](../../src/Rask.Core/Browser/IPermissions.cs)
- [Capability matrix](../browser-capabilities.md)
- [Browser APIs — the narrative map](../browser-apis.md)
