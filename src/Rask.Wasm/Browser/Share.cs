using Microsoft.JSInterop;
using Rask.Core.Browser;

namespace Rask.Wasm.Browser;

/// <summary>Default <see cref="IShare" />, backed by the Web Share API via the unified <see cref="IJSRuntime" />.</summary>
public sealed class Share(IJSRuntime js) : IShare
{
    /// <inheritdoc />
    public ValueTask ShareAsync(ShareData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return js.InvokeVoidAsync("navigator.share", data);
    }

    /// <inheritdoc />
    public async ValueTask<bool> CanShareAsync(ShareData? data = null)
    {
        try
        {
            // navigator.canShare() (no arg) reports whether sharing is supported; with data it checks
            // that specific payload. An undefined navigator.canShare faults — treat that as "can't".
            return await js.InvokeAsync<bool>("navigator.canShare", data ?? new ShareData()).ConfigureAwait(false);
        }
        catch (JSException)
        {
            return false;
        }
    }
}
