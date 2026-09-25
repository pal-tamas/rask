using System.Diagnostics.CodeAnalysis;
using Microsoft.JSInterop;

namespace Rask.Core.Browser;

/// <summary>
///     Default <see cref="IIntersectionObserver" />, backed by the unified <see cref="IJSRuntime" />. The
///     element is handed across as an <see cref="ElementRef" /> (resolved to the live node by the JSON
///     reviver); the framework's <c>__raskIntersect</c> helper holds the live <c>IntersectionObserver</c>
///     and calls back into <see cref="IntersectionInterop.Changed" /> per change.
/// </summary>
public sealed class IntersectionObserverService : IIntersectionObserver
{
    private readonly IJSRuntime _js;

    // Root IntersectionInterop's [JSInvokable] for the WASM trimmer — it's reached only via the JS
    // DotNetDispatcher (reflection), so without this the Changed method could be trimmed away.
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicMethods, typeof(IntersectionInterop))]
    public IntersectionObserverService(IJSRuntime js) => _js = js;

    /// <inheritdoc />
    public async ValueTask<IAsyncDisposable> ObserveAsync(
        ElementRef element, Func<IntersectionEntry, Task> onChange, IntersectionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(onChange);

        var id = IntersectionInterop.Register(onChange);
        try
        {
            await _js.InvokeVoidAsync("__raskIntersect.observe", id, element, options?.Thresholds, options?.RootMargin);
        }
        catch
        {
            IntersectionInterop.Unregister(id);
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
            IntersectionInterop.Unregister(id);
            await js.InvokeVoidAsync("__raskIntersect.unobserve", id);
        }
    }
}
