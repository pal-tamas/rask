# IBadge

> Set/clear the count on the installed app icon.

- **Wraps:** Badging API
- **Home:** `Rask.Core.Browser` (all hosts)
- **Shape:** one-shot
- **Availability:** Web/Server ✅ · PWA/WASM ✅ · Native ✅ ★
- **Native backend:** SetBadgeCount / badge notification

`setAppBadge` needs an installed PWA instance. In the native shell it resolves to the real app-icon badge:
iOS uses `UNUserNotificationCenter.SetBadgeCount` (numeric only — there is no numberless dot, so `SetAsync(null)`
maps to no badge); Android has no universal app-icon badge API, so the count rides a silent low-importance
notification's number (`SetAsync(null)`/`SetAsync(0)` shows a plain dot, `ClearAsync` cancels it) — which means
the Android badge needs `POST_NOTIFICATIONS` on API 33+, and the exact rendering is launcher-dependent.

## See also

- Source: [`IBadge.cs`](../../src/Rask.Core/Browser/IBadge.cs)
- [Capability matrix](../browser-capabilities.md)
- [Browser APIs — the narrative map](../browser-apis.md)
