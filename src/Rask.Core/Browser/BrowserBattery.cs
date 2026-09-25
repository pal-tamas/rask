using System.Diagnostics.CodeAnalysis;
using Microsoft.JSInterop;

namespace Rask.Core.Browser;

/// <summary>
///     Default <see cref="IBattery" />, backed by the unified <see cref="IJSRuntime" />. The framework's
///     <c>__raskBattery</c> helper reads <c>navigator.getBattery()</c> and, for a watch, adds the
///     <c>levelchange</c>/<c>chargingchange</c> listeners under the C#-minted id and pushes each update into
///     <see cref="BatteryInterop" />.
/// </summary>
public sealed class BrowserBattery : IBattery
{
    private readonly IJSRuntime _js;

    // Root BatteryInterop's [JSInvokable] for the WASM trimmer — it's reached only via the JS
    // DotNetDispatcher (reflection), so without this the Changed method could be trimmed away.
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicMethods, typeof(BatteryInterop))]
    public BrowserBattery(IJSRuntime js) => _js = js;

    /// <inheritdoc />
    public ValueTask<bool> IsSupportedAsync() => _js.InvokeAsync<bool>("__raskBattery.isSupported");

    /// <inheritdoc />
    public ValueTask<BatteryStatus?> GetStatusAsync() =>
        _js.InvokeAsync<BatteryStatus?>("__raskBattery.getStatus");

    /// <inheritdoc />
    public async ValueTask<IAsyncDisposable> WatchAsync(Func<BatteryStatus, Task> onChange)
    {
        ArgumentNullException.ThrowIfNull(onChange);

        // Register before adding the JS listeners so no early change races ahead of the handler.
        var id = BatteryInterop.Register(onChange);
        try
        {
            await _js.InvokeVoidAsync("__raskBattery.watch", id);
        }
        catch
        {
            BatteryInterop.Unregister(id);
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
            BatteryInterop.Unregister(id);
            await js.InvokeVoidAsync("__raskBattery.clear", id);
        }
    }
}
