using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using Microsoft.JSInterop;

namespace Rask.Core.Browser;

/// <summary>One DOM-mutation notification for an observed element.</summary>
/// <param name="Type">What changed: <c>"childList"</c>, <c>"attributes"</c>, or <c>"characterData"</c>.</param>
/// <param name="AddedCount">Number of nodes added in this record (<c>addedNodes.length</c>).</param>
/// <param name="RemovedCount">Number of nodes removed in this record (<c>removedNodes.length</c>).</param>
/// <param name="AttributeName">The changed attribute's name, for an <c>"attributes"</c> record; otherwise <c>null</c>.</param>
public sealed record MutationEntry(string Type, int AddedCount, int RemovedCount, string? AttributeName);

/// <summary>Tuning for an observation (the <c>MutationObserver</c> options). At least one of
/// <see cref="ChildList" />, <see cref="Attributes" />, or <see cref="CharacterData" /> must be true.</summary>
public sealed record MutationOptions
{
    /// <summary>Observe additions/removals of child nodes (default <c>true</c>).</summary>
    public bool ChildList { get; init; } = true;

    /// <summary>Observe attribute changes.</summary>
    public bool Attributes { get; init; }

    /// <summary>Observe changes to text content (<c>characterData</c>).</summary>
    public bool CharacterData { get; init; }

    /// <summary>Extend observation to the entire subtree, not just the direct target.</summary>
    public bool Subtree { get; init; }

    /// <summary>When set, only these attribute names are observed (implies <see cref="Attributes" />).</summary>
    public string[]? AttributeFilter { get; init; }
}

/// <summary>
///     Typed access to the Mutation Observer API
///     (<see href="https://developer.mozilla.org/en-US/docs/Web/API/MutationObserver" />) — be notified
///     when an element's children, attributes, or text content change, e.g. to react to DOM written by a
///     third-party script or a portal you don't own. Works on <b>both transports</b>; inject it through a
///     component constructor. Completes the observer family alongside <see cref="IIntersectionObserver" />
///     and <see cref="IResizeObserver" />.
/// </summary>
/// <remarks>
///     <para>
///         The browser <b>pushes</b> each change to the C# callback (via a static <c>[JSInvokable]</c>, so
///         one wiring serves both transports). Observe from a lifecycle hook and dispose the returned
///         handle on unmount. A callback that updates state should call <c>StateHasChanged()</c> — the
///         same pattern as subscribing to a background feed (it's a subscription, not a render/binding
///         callback, so RASK026 doesn't apply).
///     </para>
///     <code>
///     private readonly ElementRef _target = ElementRef.New();
///     protected override Component? Render() => Div(Ref: _target)[ ... ];
///     protected override async Task OnRenderedAsync(bool first)
///     {
///         if (!first) return;
///         _obs = await observer.ObserveAsync(_target, m => { _count++; StateHasChanged(); return Task.CompletedTask; },
///             new MutationOptions { ChildList = true, Attributes = true, Subtree = true });
///     }
///     </code>
/// </remarks>
public interface IMutationObserver
{
    /// <summary>
    ///     Observes <paramref name="element" /> and invokes <paramref name="onChange" /> whenever its
    ///     DOM (children, attributes, or text — per <paramref name="options" />) changes. Dispose the
    ///     returned handle to stop observing.
    /// </summary>
    ValueTask<IAsyncDisposable> ObserveAsync(
        ElementRef element, Func<MutationEntry, Task> onChange, MutationOptions? options = null);
}

/// <summary>
///     Infrastructure for <see cref="IMutationObserver" /> — routes a pushed mutation back to the right
///     C# callback by observation id. <b>Not for application use;</b> invoked only by the framework's
///     <c>__raskMutation</c> JS helper via <c>window.DotNet.invokeMethodAsync</c>.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class MutationInterop
{
    private static int _nextId;
    private static readonly ConcurrentDictionary<int, Func<MutationEntry, Task>> Handlers = new();

    internal static int Register(Func<MutationEntry, Task> handler)
    {
        var id = Interlocked.Increment(ref _nextId);
        Handlers[id] = handler;
        return id;
    }

    internal static void Unregister(int id) => Handlers.TryRemove(id, out _);

    /// <summary>Infrastructure. Invoked by the JS bridge when an observed element mutates; do not call.</summary>
    [JSInvokable("RaskMutationChanged")]
    public static Task Changed(int id, MutationEntry entry) =>
        Handlers.TryGetValue(id, out var handler) ? handler(entry) : Task.CompletedTask;
}

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
        var id = MutationInterop.Register(onChange);
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
