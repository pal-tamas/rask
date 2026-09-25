namespace Rask.Wasm.Browser;

/// <summary>One inbound HID input report.</summary>
/// <param name="ReportId">The report id (0 when the device's reports are unnumbered).</param>
/// <param name="Data">The report payload bytes (excluding the report id).</param>
public sealed record HidInputReport(int ReportId, byte[] Data);
