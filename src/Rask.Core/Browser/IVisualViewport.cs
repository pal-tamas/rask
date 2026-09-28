namespace Rask.Core.Browser;

/// <summary>
///     Typed access to the Visual Viewport API
///     (<see href="https://developer.mozilla.org/en-US/docs/Web/API/VisualViewport" />) — read the region
///     of the page that's actually visible, e.g. to keep an input above the on-screen keyboard or react to
///     pinch-zoom. Works on <b>both transports</b>; inject it through a component constructor and read from
///     an event handler or lifecycle hook.
/// </summary>
/// <remarks>
///     A one-shot snapshot at call time, not a live subscription — re-read it when you need a fresh value
///     (e.g. after a resize). Gate on <see cref="IsSupportedAsync" />; <see cref="GetAsync" /> returns
///     <c>null</c> on the rare browser without the API.
/// </remarks>
public interface IVisualViewport
{
    /// <summary>Whether the browser exposes the visual viewport (<c>window.visualViewport</c>).</summary>
    ValueTask<bool> IsSupportedAsync();

    /// <summary>Reads the current <see cref="VisualViewport" /> snapshot, or <c>null</c> when unsupported.</summary>
    ValueTask<VisualViewport?> GetAsync();
}
