# INotifications

> Show a local notification from the page.

- **Wraps:** Notifications API
- **MDN:** [Notifications API](https://developer.mozilla.org/en-US/docs/Web/API/Notifications_API)
- **Home:** `Rask.Core.Browser` (all hosts)
- **Shape:** one-shot
- **Availability:** Web/Server ✅ · PWA/WASM ✅

Transport-agnostic; the JS helper ships on Server only under `AddRaskPwa`. `Tag` maps to the notification
identifier, so a same-tag notification replaces the previous one.

> **A permission the user has denied does not re-prompt.** `RequestPermissionAsync()` returns `Denied` rather
> than pretending a prompt would help, and `ShowAsync` throws as it does for any ungranted permission — the
> way back is the browser's site settings.

## See also

- Source: [`INotifications.cs`](../../src/Rask.Core/Browser/INotifications.cs)
- [Capability matrix](../browser-capabilities.md)
- [Browser APIs — the narrative map](../browser-apis.md)
