# ISpeechSynthesis

> Speak text aloud; cancel the queue.

- **Wraps:** SpeechSynthesis API
- **MDN:** [SpeechSynthesis](https://developer.mozilla.org/en-US/docs/Web/API/SpeechSynthesis)
- **Home:** `Rask.Core.Browser` (all hosts)
- **Shape:** one-shot
- **Availability:** Web/Server ✅ · PWA/WASM ✅

Uses the browser's own `speechSynthesis` voices; the voice list is populated asynchronously, so query it after the first `voiceschanged`.

## See also

- Source: [`ISpeechSynthesis.cs`](../../src/Rask.Core/Browser/ISpeechSynthesis.cs)
- [Capability matrix](../browser-capabilities.md)
- [Browser APIs — the narrative map](../browser-apis.md)
