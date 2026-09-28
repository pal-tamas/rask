namespace Rask.Wasm.Browser;

/// <summary>A handle to one Bluetooth device. Dispose (or <see cref="DisconnectAsync" />) to drop the connection.</summary>
public interface IBluetoothDevice : IAsyncDisposable
{
    /// <summary>The device's identity.</summary>
    BluetoothDeviceInfo Info { get; }

    /// <summary>Connects to the device's GATT server (may be called again after <see cref="DisconnectAsync" />).</summary>
    ValueTask ConnectAsync();

    /// <summary>
    ///     Disconnects the GATT server but keeps the handle usable — call <see cref="ConnectAsync" /> to
    ///     reconnect. Previously-resolved <see cref="IBluetoothCharacteristic" /> handles are invalidated (GATT
    ///     disconnect drops them), so re-resolve via <see cref="GetCharacteristicAsync" /> after reconnecting.
    ///     Any <see cref="WatchDisconnectAsync" /> callback also fires (the browser raises its disconnect
    ///     event). Dispose the device to release it entirely.
    /// </summary>
    ValueTask DisconnectAsync();

    /// <summary>Whether the GATT server is currently connected.</summary>
    ValueTask<bool> IsConnectedAsync();

    /// <summary>
    ///     Resolves a characteristic by its service and characteristic UUID (name like <c>"battery_service"</c>
    ///     / <c>"battery_level"</c>, or a full UUID). The device must be connected.
    /// </summary>
    ValueTask<IBluetoothCharacteristic> GetCharacteristicAsync(string serviceUuid, string characteristicUuid);

    /// <summary>
    ///     Invokes <paramref name="onDisconnect" /> if the GATT server disconnects (e.g. the device goes out of
    ///     range or is turned off). Dispose the returned handle to stop listening.
    /// </summary>
    ValueTask<IAsyncDisposable> WatchDisconnectAsync(Func<Task> onDisconnect);
}
