using Microsoft.JSInterop;

namespace Rask.Core.Browser;

/// <summary>A snapshot of the screen / display (<c>window.screen</c> plus <c>devicePixelRatio</c>).</summary>
/// <param name="Width">Total screen width in CSS pixels (<c>screen.width</c>).</param>
/// <param name="Height">Total screen height in CSS pixels (<c>screen.height</c>).</param>
/// <param name="AvailWidth">Width available to the app, minus OS chrome (<c>screen.availWidth</c>).</param>
/// <param name="AvailHeight">Height available to the app, minus OS chrome (<c>screen.availHeight</c>).</param>
/// <param name="ColorDepth">Bits per pixel (<c>screen.colorDepth</c>, typically 24).</param>
/// <param name="PixelRatio">Device pixels per CSS pixel (<c>devicePixelRatio</c>; &gt; 1 on HiDPI/retina).</param>
public sealed record ScreenInfo(
    int Width,
    int Height,
    int AvailWidth,
    int AvailHeight,
    int ColorDepth,
    double PixelRatio);

/// <summary>
///     Typed access to screen / display information (the Screen API,
///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/Screen" />) — read the display size,
///     color depth, and device pixel ratio, e.g. to pick image resolution (retina) or for analytics. Works
///     on <b>both transports</b>; inject it through a component constructor and read from an event handler
///     or lifecycle hook.
/// </summary>
/// <remarks>
///     This is a one-shot snapshot at call time, not a live subscription — re-read it when you need a fresh
///     answer (e.g. after a window move between displays). <c>window.screen</c> is universally supported, so
///     no capability gate is needed.
/// </remarks>
public interface IScreenInfo
{
    /// <summary>Reads the current <see cref="ScreenInfo" /> snapshot.</summary>
    ValueTask<ScreenInfo> GetAsync();
}

/// <summary>
///     Default <see cref="IScreenInfo" />, backed by the unified <see cref="IJSRuntime" />. The read goes
///     through the framework's <c>__raskApi.screen</c> helper, which returns a plain snapshot object (the
///     properties live on <c>screen</c> and <c>window</c>, so a single call keeps them consistent).
/// </summary>
public sealed class ScreenInfoReader(IJSRuntime js) : IScreenInfo
{
    /// <inheritdoc />
    public ValueTask<ScreenInfo> GetAsync() => js.InvokeAsync<ScreenInfo>("__raskApi.screen");
}
