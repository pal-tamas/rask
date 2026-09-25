using System.Diagnostics.CodeAnalysis;
using Microsoft.JSInterop;

namespace Rask.Core.Browser;

/// <summary>
///     Default <see cref="IDeviceMotion" />, backed by the unified <see cref="IJSRuntime" />. The framework's
///     <c>__raskDeviceMotion</c> helper adds the <c>devicemotion</c> listener under the C#-minted id and pushes
///     each reading into <see cref="DeviceMotionInterop" />.
/// </summary>
public sealed class DeviceMotion : IDeviceMotion
{
    private readonly IJSRuntime _js;

    // Root DeviceMotionInterop's [JSInvokable] for the WASM trimmer — it's reached only via the JS
    // DotNetDispatcher (reflection), so without this the Reading method could be trimmed away.
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicMethods, typeof(DeviceMotionInterop))]
    public DeviceMotion(IJSRuntime js) => _js = js;

    /// <inheritdoc />
    public ValueTask<bool> IsSupportedAsync() => _js.InvokeAsync<bool>("__raskDeviceMotion.isSupported");

    /// <inheritdoc />
    public async ValueTask<SensorPermissionState> RequestPermissionAsync() =>
        string.Equals(await _js.InvokeAsync<string>("__raskDeviceMotion.requestPermission"), "granted", StringComparison.Ordinal)
            ? SensorPermissionState.Granted
            : SensorPermissionState.Denied;

    /// <inheritdoc />
    public async ValueTask<IAsyncDisposable> WatchAsync(Func<MotionReading, Task> onReading)
    {
        ArgumentNullException.ThrowIfNull(onReading);

        var id = DeviceMotionInterop.Register(onReading);
        try
        {
            await _js.InvokeVoidAsync("__raskDeviceMotion.watch", id);
        }
        catch
        {
            DeviceMotionInterop.Unregister(id);
            throw;
        }

        return new Watch(_js, id);
    }

    private sealed class Watch(IJSRuntime js, int id) : IAsyncDisposable
    {
        private bool _disposed;

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            DeviceMotionInterop.Unregister(id);
            await js.InvokeVoidAsync("__raskDeviceMotion.clear", id);
        }
    }
}
