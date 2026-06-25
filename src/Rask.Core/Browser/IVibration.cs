using Microsoft.JSInterop;

namespace Rask.Core.Browser;

/// <summary>
///     Typed access to the Vibration API
///     (<see href="https://developer.mozilla.org/en-US/docs/Web/API/Navigator/vibrate" />) — pulse the
///     device's vibration hardware. Inject it through a component constructor and call from an event
///     handler. Only effective on supporting devices (typically mobile) and after a user gesture; a
///     no-op elsewhere.
/// </summary>
public interface IVibration
{
    /// <summary>
    ///     Vibrates following <paramref name="pattern" /> — alternating vibrate/pause durations in
    ///     milliseconds (e.g. <c>200, 100, 200</c>). A single value is one buzz. Returns <c>false</c> if
    ///     the device can't vibrate or the pattern is rejected.
    /// </summary>
    ValueTask<bool> VibrateAsync(params int[] pattern);

    /// <summary>Cancels any in-progress vibration (<c>navigator.vibrate(0)</c>).</summary>
    ValueTask<bool> CancelAsync();
}

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
