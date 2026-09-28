using Microsoft.JSInterop;

namespace Rask.Core.Browser;

/// <summary>
///     Default <see cref="IWakeLock" />, backed by the unified <see cref="IJSRuntime" />. A
///     <c>WakeLockSentinel</c> is a live JS object <see cref="IJSRuntime" /> can't hand back, so the
///     framework's <c>__raskWakeLock</c> helper keeps it in a small registry and returns an integer id;
///     the sentinel releases by id on dispose.
/// </summary>
public sealed class WakeLock(IJSRuntime js) : IWakeLock
{
    /// <inheritdoc />
    public ValueTask<bool> IsSupportedAsync() => js.InvokeAsync<bool>("__raskWakeLock.isSupported");

    /// <inheritdoc />
    public async ValueTask<IWakeLockSentinel> RequestAsync()
    {
        var id = await js.InvokeAsync<int>("__raskWakeLock.request");
        return new Sentinel(js, id);
    }

    private sealed class Sentinel(IJSRuntime js, int id) : IWakeLockSentinel
    {
        private bool _released;

        public ValueTask DisposeAsync()
        {
            if (_released)
            {
                return ValueTask.CompletedTask;
            }

            _released = true;
            return js.InvokeVoidAsync("__raskWakeLock.release", id);
        }
    }
}
