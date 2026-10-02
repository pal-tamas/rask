# ISpeechRecognition

> Dictation — turn spoken audio into text.

- **Wraps:** SpeechRecognition API (`webkitSpeechRecognition`)
- **MDN:** [SpeechRecognition](https://developer.mozilla.org/en-US/docs/Web/API/SpeechRecognition)
- **Home:** `Rask.Core.Browser` (all hosts)
- **Shape:** subscription (pushes each result to a callback)
- **Availability:** Web/Server ✅ (Chromium-only) · PWA/WASM ✅ (Chromium-only)

Speaking text aloud is `SpeechSynthesis.Speak(…)` in [`Rask.Web`](../web-apis.md#keeping-an-object).
Call `StartAsync(onResult, options)` from a user gesture — it prompts for microphone access and returns an
`IAsyncDisposable`; dispose it to stop listening and release the microphone. Each `RecognitionResult` carries the `Transcript`, whether it
`IsFinal`, and a `Confidence`. `SpeechRecognitionOptions` sets the `Lang` (BCP-47), `Continuous` listening
(restarts after each utterance until disposed), and `InterimResults` (emit live hypotheses as well as final
transcripts). Browser support is Chromium-family; gate on `IsSupportedAsync`. Needs microphone permission.

## See also

- Source: [`ISpeechRecognition.cs`](../../src/Rask.Core/Browser/ISpeechRecognition.cs)
- [`SpeechSynthesis` in Rask.Web](../web-apis.md#keeping-an-object) — the other half of the pair
- [Capability matrix](../browser-capabilities.md)
- [Browser APIs — the narrative map](../browser-apis.md)
