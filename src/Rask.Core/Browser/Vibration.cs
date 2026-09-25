using Microsoft.JSInterop;

namespace Rask.Core.Browser;

/// <summary>Default <see cref="IVibration" />, backed by the unified <see cref="IJSRuntime" />.</summary>
public sealed class Vibration(IJSRuntime js) : IVibration
{
    /// <inheritdoc />
    public ValueTask<bool> VibrateAsync(params int[] pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        // Pass the pattern array as a single argument so the call is navigator.vibrate([...]).
        return js.InvokeAsync<bool>("navigator.vibrate", (object)pattern);
    }

    /// <inheritdoc />
    public ValueTask<bool> CancelAsync() => js.InvokeAsync<bool>("navigator.vibrate", 0);
}
