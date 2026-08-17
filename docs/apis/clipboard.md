# IClipboard

> Copy text to, and read text from, the system clipboard.

- **Wraps:** Async Clipboard API
- **MDN:** [Clipboard API](https://developer.mozilla.org/en-US/docs/Web/API/Clipboard_API)
- **Home:** `Rask.Core.Browser` (all hosts)
- **Shape:** one-shot
- **Availability:** Web/Server ✅ · PWA/WASM ✅ · Native ✅
- **Native backend:** UIPasteboard / ClipboardManager

In the browser, reads need a user gesture and/or a `clipboard-read` grant; the native backend has neither restriction.

## See also

- Source: [`IClipboard.cs`](../../src/Rask.Core/Browser/IClipboard.cs)
- [Capability matrix](../browser-capabilities.md)
- [Browser APIs — the narrative map](../browser-apis.md)
