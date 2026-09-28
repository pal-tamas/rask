using Microsoft.JSInterop;

namespace Rask.Core.Browser;

/// <summary>
///     Default <see cref="IWebLocks" />, backed by the unified <see cref="IJSRuntime" />. The live
///     <c>Lock</c> is opaque to C#, so the framework's <c>__raskLocks</c> helper holds it under a
///     C#-minted id: <c>request</c> resolves as soon as the lock is granted (or <c>false</c> when
///     <c>ifAvailable</c> can't grant it), and the helper keeps the lock until <c>release</c> is called —
///     which this wrapper does once <c>work</c> completes. No <c>[JSInvokable]</c> callback is needed:
///     C# controls the hold purely by when it calls <c>release</c>.
/// </summary>
public sealed class WebLocks(IJSRuntime js) : IWebLocks
{
    private static int _nextId;

    /// <inheritdoc />
    public ValueTask<bool> IsSupportedAsync() => js.InvokeAsync<bool>("__raskLocks.isSupported");

    /// <inheritdoc />
    public async ValueTask RequestAsync(string name, Func<Task> work, LockMode mode = LockMode.Exclusive)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(work);

        // A non-ifAvailable request resolves true once the lock is granted (it waits as long as needed).
        var id = Interlocked.Increment(ref _nextId);
        await js.InvokeAsync<bool>("__raskLocks.request", id, name, ModeString(mode), false);
        try
        {
            await work();
        }
        finally
        {
            await js.InvokeVoidAsync("__raskLocks.release", id);
        }
    }

    /// <inheritdoc />
    public async ValueTask<bool> TryRequestAsync(string name, Func<Task> work, LockMode mode = LockMode.Exclusive)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(work);

        var id = Interlocked.Increment(ref _nextId);
        var granted = await js.InvokeAsync<bool>("__raskLocks.request", id, name, ModeString(mode), true);
        if (!granted)
        {
            return false; // ifAvailable: the lock was already held — work never runs, nothing to release.
        }

        try
        {
            await work();
        }
        finally
        {
            await js.InvokeVoidAsync("__raskLocks.release", id);
        }

        return true;
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<LockInfo>> QueryAsync()
    {
        var locks = await js.InvokeAsync<LockInfo[]>("__raskLocks.query");
        return locks ?? [];
    }

    // Map the enum to the raw API string C#-side, so nothing enum-shaped crosses the JS bridge.
    private static string ModeString(LockMode mode) => mode == LockMode.Shared ? "shared" : "exclusive";
}
