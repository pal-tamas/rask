namespace Rask.Core.Browser;

// Wire shape for __raskApi.network — effectiveType arrives as the browser's lowercase string and is
// mapped to the typed enum in C#. Registered for trim-safe source-gen in RaskBrowserJsonContext.
internal sealed record NetworkReading(string? EffectiveType, double Downlink, double Rtt, bool SaveData);
