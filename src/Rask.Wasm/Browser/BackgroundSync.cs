using System.Diagnostics.CodeAnalysis;
using Microsoft.JSInterop;

namespace Rask.Wasm.Browser;

/// <summary>
///     Default <see cref="IBackgroundSync" />, backed by the unified <see cref="IJSRuntime" /> and the
///     framework's WASM-only <c>__raskSync</c> helper.
/// </summary>
public sealed class BackgroundSync : IBackgroundSync
{
    private readonly IJSRuntime _js;

    // Root BackgroundSyncInterop's [JSInvokable] for the WASM trimmer — it is reached only through the JS
    // DotNetDispatcher (reflection), so without this the Fired method could be trimmed away.
    /// <summary>
    ///     Creates the service. Registered for you — inject <see cref="IBackgroundSync" /> rather than
    ///     constructing this.
    /// </summary>
    /// <param name="js">The JS interop runtime the wrapper calls through.</param>
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicMethods, typeof(BackgroundSyncInterop))]
    public BackgroundSync(IJSRuntime js) => _js = js;

    /// <inheritdoc />
    public ValueTask<bool> IsSupported() => _js.InvokeAsync<bool>("__raskSync.supported");

    /// <inheritdoc />
    public ValueTask<bool> IsPeriodicSupported() => _js.InvokeAsync<bool>("__raskSync.periodicSupported");

    /// <inheritdoc />
    public ValueTask<bool> RequestSync(string tag)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);
        return _js.InvokeAsync<bool>("__raskSync.request", tag);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<string>> GetPendingTags() =>
        await _js.InvokeAsync<string[]>("__raskSync.tags").ConfigureAwait(false) ?? [];

    /// <inheritdoc />
    public ValueTask<string> GetPeriodicPermission() =>
        _js.InvokeAsync<string>("__raskSync.periodicPermission");

    /// <inheritdoc />
    public ValueTask<bool> RequestPeriodicSync(string tag, TimeSpan minInterval)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(minInterval, TimeSpan.Zero);
        return _js.InvokeAsync<bool>("__raskSync.requestPeriodic", tag, minInterval.TotalMilliseconds);
    }

    /// <inheritdoc />
    public ValueTask UnregisterPeriodic(string tag)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);
        return _js.InvokeVoidAsync("__raskSync.unregisterPeriodic", tag);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<string>> GetPeriodicTags() =>
        await _js.InvokeAsync<string[]>("__raskSync.periodicTags").ConfigureAwait(false) ?? [];

    /// <inheritdoc />
    public async ValueTask<IAsyncDisposable> OnSync(Func<BackgroundSyncEvent, Task> onSync)
    {
        ArgumentNullException.ThrowIfNull(onSync);

        var id = BackgroundSyncInterop.Register(onSync);
        try
        {
            // Idempotent on the JS side, and it is what releases anything the helper buffered during boot —
            // so the first subscriber sees a sync that landed before the runtime was ready.
            await _js.InvokeVoidAsync("__raskSync.listen").ConfigureAwait(false);
        }
        catch
        {
            BackgroundSyncInterop.Unregister(id);
            throw;
        }

        return new Subscription(id);
    }

    private sealed class Subscription(int id) : IAsyncDisposable
    {
        private bool _disposed;

        public ValueTask DisposeAsync()
        {
            if (!_disposed)
            {
                _disposed = true;
                BackgroundSyncInterop.Unregister(id);
            }

            // Nothing to tear down in JS: the helper keeps one message listener for the page's lifetime and
            // C# owns the fan-out, so unsubscribing is purely local.
            return ValueTask.CompletedTask;
        }
    }
}
