namespace Rask.Wasm.Browser;

// Wire shape for __raskOrientation.get — the browser's hyphenated string plus angle, mapped to the
// typed OrientationInfo in C#. Registered for trim-safe source-gen in RaskWasmBrowserJsonContext.
internal sealed record OrientationReading(string? Type, int Angle);
