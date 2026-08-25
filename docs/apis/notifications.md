# INotifications

> Show a local notification from the page.

- **Wraps:** Notifications API
- **MDN:** [Notifications API](https://developer.mozilla.org/en-US/docs/Web/API/Notifications_API)
- **Home:** `Rask.Core.Browser` (all hosts)
- **Shape:** one-shot
- **Availability:** Web/Server ✅ · PWA/WASM ✅

Transport-agnostic; the JS helper ships on Server only under `AddRaskPwa`. In the native shell it resolves to a
native backend — a WebView has no `Notification` API. On Android 33+ this needs the `POST_NOTIFICATIONS`
permission (declared in the manifest + granted at runtime, as the sample/template activities do); `Tag` maps to
the notification identifier so a same-tag notification replaces the previous one.

> **On Android, `PermissionAsync()` also reflects the app's notification toggle in Settings.** That switch is
> independent of `POST_NOTIFICATIONS` and exists on every supported version, including the pre-33 ones where
> there is no runtime permission at all — and with it off, posting a notification is a **silent** no-op rather
> than an error. So an app the user has muted reports `Denied` (not `Granted`, and not `Default`): the way back
> is the Settings screen, not a prompt, which is what `Denied` means here. `RequestPermissionAsync()` returns
> `Denied` for it rather than pretending a prompt would help, and `ShowAsync` throws as it does for any
> ungranted permission.

## See also

- Source: [`INotifications.cs`](../../src/Rask.Core/Browser/INotifications.cs)
- [Capability matrix](../browser-capabilities.md)
- [Browser APIs — the narrative map](../browser-apis.md)
