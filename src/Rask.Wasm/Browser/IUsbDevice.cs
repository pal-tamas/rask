namespace Rask.Wasm.Browser;

/// <summary>A handle to one USB device. Dispose (or <see cref="CloseAsync" />) to release it.</summary>
public interface IUsbDevice : IAsyncDisposable
{
    /// <summary>What the device reports about itself (available without opening).</summary>
    UsbDeviceInfo Info { get; }

    /// <summary>Opens the device for I/O.</summary>
    ValueTask OpenAsync();

    /// <summary>Selects the device configuration by its <c>configurationValue</c>.</summary>
    ValueTask SelectConfigurationAsync(int configurationValue);

    /// <summary>Claims exclusive use of an interface by number — required before transferring on it.</summary>
    ValueTask ClaimInterfaceAsync(int interfaceNumber);

    /// <summary>Releases a previously claimed interface.</summary>
    ValueTask ReleaseInterfaceAsync(int interfaceNumber);

    /// <summary>Reads up to <paramref name="length" /> bytes from a bulk/interrupt IN endpoint.</summary>
    ValueTask<UsbTransferResult> TransferInAsync(int endpointNumber, int length);

    /// <summary>Writes <paramref name="data" /> to a bulk/interrupt OUT endpoint.</summary>
    ValueTask<UsbOutTransferResult> TransferOutAsync(int endpointNumber, byte[] data);

    /// <summary>Runs a control IN transfer, reading up to <paramref name="length" /> bytes.</summary>
    ValueTask<UsbTransferResult> ControlTransferInAsync(UsbControlTransferParams setup, int length);

    /// <summary>Runs a control OUT transfer, writing <paramref name="data" />.</summary>
    ValueTask<UsbOutTransferResult> ControlTransferOutAsync(UsbControlTransferParams setup, byte[] data);

    /// <summary>Closes the device, releasing it for other applications.</summary>
    ValueTask CloseAsync();
}
