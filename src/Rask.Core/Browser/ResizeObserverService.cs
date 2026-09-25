using System.Diagnostics.CodeAnalysis;
using Microsoft.JSInterop;

namespace Rask.Core.Browser;

/// <summary>
///     Default <see cref="IResizeObserver" />, backed by the unified <see cref="IJSRuntime" />. The element
///     is handed across as an <see cref="ElementRef" /> (resolved by the JSON reviver); the framework's
///     <c>__raskResize</c> helper holds the live <c>ResizeObserver</c> and calls back into
///     <see cref="ResizeInterop.Changed" /> per change.
/// </summary>
public sealed class ResizeObserverService : IResizeObserver
{
    private readonly IJSRuntime _js;

    // Root ResizeInterop's [JSInvokable] for the WASM trimmer — it's reached only via the JS
    // DotNetDispatcher (reflection), so without this the Changed method could be trimmed away.
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicMethods, typeof(ResizeInterop))]
    public ResizeObserverService(IJSRuntime js) => _js = js;

    /// <inheritdoc />
    public async ValueTask<IAsyncDisposable> ObserveAsync(ElementRef element, Func<ResizeEntry, Task> onChange)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(onChange);

        var id = ResizeInterop.Register(onChange);
        try
        {
            await _js.InvokeVoidAsync("__raskResize.observe", id, element);
        }
        catch
        {
            ResizeInterop.Unregister(id);
            throw;
        }

        return new Observation(_js, id);
    }

    private sealed class Observation(IJSRuntime js, int id) : IAsyncDisposable
    {
        private bool _disposed;

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            ResizeInterop.Unregister(id);
            await js.InvokeVoidAsync("__raskResize.unobserve", id);
        }
    }
}
