namespace Rask.Wasm.Browser;

/// <summary>A handle to one HID device. Dispose (or <see cref="CloseAsync" />) to release it.</summary>
public interface IHidDevice : IAsyncDisposable
{
    /// <summary>What the device reports about itself.</summary>
    HidDeviceInfo Info { get; }

    /// <summary>Opens the device for I/O (required before sending reports or receiving input reports).</summary>
    ValueTask OpenAsync();

    /// <summary>Closes the device, releasing it for other applications.</summary>
    ValueTask CloseAsync();

    /// <summary>Sends an output report (<paramref name="reportId" /> 0 when the device's reports are unnumbered).</summary>
    ValueTask SendReportAsync(int reportId, byte[] data);

    /// <summary>Sends a feature report.</summary>
    ValueTask SendFeatureReportAsync(int reportId, byte[] data);

    /// <summary>Reads a feature report, returning its payload bytes.</summary>
    ValueTask<byte[]> ReceiveFeatureReportAsync(int reportId);

    /// <summary>
    ///     Starts delivering this device's input reports to <paramref name="onReport" />;
    ///     <paramref name="onDisconnect" /> (optional) fires once if the device is unplugged. Dispose the
    ///     returned handle to stop. The device must be open.
    /// </summary>
    ValueTask<IAsyncDisposable> WatchInputReportsAsync(
        Func<HidInputReport, Task> onReport, Func<Task>? onDisconnect = null);
}
