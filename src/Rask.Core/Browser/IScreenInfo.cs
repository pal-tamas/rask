namespace Rask.Core.Browser;

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
