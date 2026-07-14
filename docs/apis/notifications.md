# INotifications

> Show a local notification from the page.

- **Wraps:** Notifications API
- **Home:** `Rask.Core.Browser` (all hosts)
- **Shape:** one-shot
- **Availability:** Web/Server ✅ · PWA/WASM ✅ · Native ✅ ★
- **Native backend:** UNUserNotificationCenter / NotificationManager

Transport-agnostic; the JS helper ships on Server only under `AddRaskPwa`. In the native shell it resolves to a
native backend — a WebView has no `Notification` API. On Android 33+ this needs the `POST_NOTIFICATIONS`
permission (declared in the manifest + granted at runtime, as the sample/template activities do); `Tag` maps to
the notification identifier so a same-tag notification replaces the previous one.

## See also

- Source: [`INotifications.cs`](../../src/Rask.Core/Browser/INotifications.cs)
- [Capability matrix](../browser-capabilities.md)
- [Browser APIs — the narrative map](../browser-apis.md)
