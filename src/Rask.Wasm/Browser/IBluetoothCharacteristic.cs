namespace Rask.Wasm.Browser;

/// <summary>A handle to one GATT characteristic. Dispose to stop any notifications and release it.</summary>
public interface IBluetoothCharacteristic : IAsyncDisposable
{
    /// <summary>Reads the characteristic's current value.</summary>
    ValueTask<byte[]> ReadAsync();

    /// <summary>Writes <paramref name="data" /> to the characteristic (with or without a response).</summary>
    ValueTask WriteAsync(byte[] data, bool withResponse = true);

    /// <summary>
    ///     Starts notifications and invokes <paramref name="onValue" /> with each new value the device pushes.
    ///     Dispose the returned handle to stop notifications.
    /// </summary>
    ValueTask<IAsyncDisposable> WatchAsync(Func<byte[], Task> onValue);
}
