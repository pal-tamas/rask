using System.Diagnostics.CodeAnalysis;
using Microsoft.JSInterop;

namespace Rask.Core.Browser;

/// <summary>
///     Default <see cref="IGamepad" />, backed by the unified <see cref="IJSRuntime" />. Each watch gets an
///     integer id; the framework's <c>__raskGamepad</c> helper runs the <c>requestAnimationFrame</c> poll and
///     calls back into <see cref="GamepadInterop.Reading" /> (a static <c>[JSInvokable]</c>, so one wiring
///     serves both transports without marshalling a <c>DotNetObjectReference</c>).
/// </summary>
public sealed class Gamepad : IGamepad
{
    private readonly IJSRuntime _js;

    // Root GamepadInterop's [JSInvokable] for the WASM trimmer — it's reached only via the JS
    // DotNetDispatcher (reflection), so without this the Reading method could be trimmed away.
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicMethods, typeof(GamepadInterop))]
    public Gamepad(IJSRuntime js) => _js = js;

    /// <inheritdoc />
    public ValueTask<bool> IsSupportedAsync() => _js.InvokeAsync<bool>("__raskGamepad.isSupported");

    /// <inheritdoc />
    public async ValueTask<IAsyncDisposable> WatchAsync(Func<GamepadReading, Task> onReading)
    {
        ArgumentNullException.ThrowIfNull(onReading);

        var id = GamepadInterop.Register(onReading);
        try
        {
            await _js.InvokeVoidAsync("__raskGamepad.watch", id);
        }
        catch
        {
            GamepadInterop.Unregister(id);
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
            GamepadInterop.Unregister(id);
            await js.InvokeVoidAsync("__raskGamepad.unwatch", id);
        }
    }
}
