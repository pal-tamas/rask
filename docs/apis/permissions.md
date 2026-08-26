# IPermissions

> Query a permission's state (`granted`/`denied`/`prompt`) before prompting.

- **Wraps:** Permissions API
- **MDN:** [Permissions API](https://developer.mozilla.org/en-US/docs/Web/API/Permissions_API)
- **Home:** `Rask.Core.Browser` (all hosts)
- **Shape:** one-shot
- **Availability:** Web/Server ✅ · PWA/WASM ✅

## Support is patchy across engines

`navigator.permissions.query` answers for different names in different engines. WebKit (Safari) answers
only for `camera` and `microphone`; `geolocation` and `notifications` throw `NotSupportedError`, and the
clipboard/persistent-storage names throw `TypeError`. An unrecognised name faults the awaited task with a
`JSException`, so gate accordingly — or catch and treat a fault as "unknown".

## See also

- Source: [`IPermissions.cs`](../../src/Rask.Core/Browser/IPermissions.cs)
- [Capability matrix](../browser-capabilities.md)
- [Browser APIs — the narrative map](../browser-apis.md)
