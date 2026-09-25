using System.Diagnostics.CodeAnalysis;
using Microsoft.JSInterop;

namespace Rask.Core.Browser;

/// <summary>
///     Default <see cref="IDeviceOrientation" />, backed by the unified <see cref="IJSRuntime" />. The
///     framework's <c>__raskDeviceOrientation</c> helper adds the <c>deviceorientation</c> listener under the
///     C#-minted id and pushes each reading into <see cref="DeviceOrientationInterop" />.
/// </summary>
public sealed class DeviceOrientation : IDeviceOrientation
{
    private readonly IJSRuntime _js;

    // Root DeviceOrientationInterop's [JSInvokable] for the WASM trimmer — it's reached only via the JS
    // DotNetDispatcher (reflection), so without this the Reading method could be trimmed away.
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicMethods, typeof(DeviceOrientationInterop))]
    public DeviceOrientation(IJSRuntime js) => _js = js;

    /// <inheritdoc />
    public ValueTask<bool> IsSupportedAsync() => _js.InvokeAsync<bool>("__raskDeviceOrientation.isSupported");

    /// <inheritdoc />
    public async ValueTask<SensorPermissionState> RequestPermissionAsync() =>
        string.Equals(await _js.InvokeAsync<string>("__raskDeviceOrientation.requestPermission"), "granted", StringComparison.Ordinal)
            ? SensorPermissionState.Granted
            : SensorPermissionState.Denied;

    /// <inheritdoc />
    public async ValueTask<IAsyncDisposable> WatchAsync(Func<OrientationReading, Task> onReading)
    {
        ArgumentNullException.ThrowIfNull(onReading);

        var id = DeviceOrientationInterop.Register(onReading);
        try
        {
            await _js.InvokeVoidAsync("__raskDeviceOrientation.watch", id);
        }
        catch
        {
            DeviceOrientationInterop.Unregister(id);
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
            DeviceOrientationInterop.Unregister(id);
            await js.InvokeVoidAsync("__raskDeviceOrientation.clear", id);
        }
    }
}
