namespace Rask.Wasm.Browser;

/// <summary>A handle to one open serial port. Dispose (or <see cref="CloseAsync" />) to stop reading and close it.</summary>
public interface ISerialPort : IAsyncDisposable
{
    /// <summary>Writes <paramref name="data" /> to the port. Concurrent writes are serialized.</summary>
    ValueTask WriteAsync(byte[] data);

    /// <summary>Stops the read loop and closes the port, releasing it for other applications.</summary>
    ValueTask CloseAsync();
}
