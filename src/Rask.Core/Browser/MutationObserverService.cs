using System.Diagnostics.CodeAnalysis;
using Microsoft.JSInterop;

namespace Rask.Core.Browser;

/// <summary>
///     Default <see cref="IMutationObserver" />, backed by the unified <see cref="IJSRuntime" />. The
///     element is handed across as an <see cref="ElementRef" /> (resolved to the live node by the JSON
///     reviver); the framework's <c>__raskMutation</c> helper holds the live <c>MutationObserver</c> and
///     calls back into <see cref="MutationInterop.Changed" /> per change.
/// </summary>
public sealed class MutationObserverService : IMutationObserver
{
    private readonly IJSRuntime _js;

    // Root MutationInterop's [JSInvokable] for the WASM trimmer — it's reached only via the JS
    // DotNetDispatcher (reflection), so without this the Changed method could be trimmed away.
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicMethods, typeof(MutationInterop))]
    public MutationObserverService(IJSRuntime js) => _js = js;

    /// <inheritdoc />
    public async ValueTask<IAsyncDisposable> ObserveAsync(
        ElementRef element, Func<MutationEntry, Task> onChange, MutationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(onChange);

        options ??= new MutationOptions();
        var id = MutationInterop.Register(_js, onChange);
        try
        {
            await _js.InvokeVoidAsync(
                "__raskMutation.observe", id, element,
                options.ChildList, options.Attributes, options.CharacterData, options.Subtree,
                options.AttributeFilter);
        }
        catch
        {
            MutationInterop.Unregister(id);
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
            MutationInterop.Unregister(id);
            await js.InvokeVoidAsync("__raskMutation.unobserve", id);
        }
    }
}
