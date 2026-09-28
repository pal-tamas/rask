using System.Diagnostics.CodeAnalysis;
using Microsoft.JSInterop;

namespace Rask.Wasm.Browser;

/// <summary>
///     Default <see cref="IIdleDetector" />, backed by the unified <see cref="IJSRuntime" />. Each watch gets
///     an integer id; the framework's <c>__raskIdle</c> helper holds the live <c>IdleDetector</c> and calls
///     back into <see cref="IdleDetectorInterop.Changed" /> (a static <c>[JSInvokable]</c> in this assembly,
///     dispatched by the WASM <c>DotNet</c> shim without a <c>DotNetObjectReference</c>).
/// </summary>
public sealed class IdleDetectorService : IIdleDetector
{
    private readonly IJSRuntime _js;

    // Root IdleDetectorInterop's [JSInvokable] for the WASM trimmer — it's reached only via the JS
    // DotNetDispatcher (reflection), so without this the Changed method could be trimmed away.
    /// <summary>
    ///     Creates the service. Registered for you — inject <see cref="IIdleDetector" /> rather than
    ///     constructing this.
    /// </summary>
    /// <param name="js">The JS interop runtime the wrapper calls through.</param>
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicMethods, typeof(IdleDetectorInterop))]
    public IdleDetectorService(IJSRuntime js) => _js = js;

    /// <inheritdoc />
    public ValueTask<bool> IsSupportedAsync() => _js.InvokeAsync<bool>("__raskIdle.isSupported");

    /// <inheritdoc />
    public ValueTask<string> RequestPermissionAsync() =>
        _js.InvokeAsync<string>("__raskIdle.requestPermission");

    /// <inheritdoc />
    public async ValueTask<IAsyncDisposable> WatchAsync(Func<IdleReading, Task> onChange, int thresholdSeconds = 60)
    {
        ArgumentNullException.ThrowIfNull(onChange);

        var id = IdleDetectorInterop.Register(onChange);
        try
        {
            await _js.InvokeVoidAsync("__raskIdle.watch", id, thresholdSeconds).ConfigureAwait(false);
        }
        catch
        {
            IdleDetectorInterop.Unregister(id);
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
            IdleDetectorInterop.Unregister(id);
            await js.InvokeVoidAsync("__raskIdle.unwatch", id).ConfigureAwait(false);
        }
    }
}
