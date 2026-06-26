using Microsoft.JSInterop;

namespace Rask.Core.Browser;

/// <summary>
///     A snapshot of the visual viewport — the portion of the page actually visible, which shrinks/offsets
///     under the on-screen keyboard and pinch-zoom (<c>window.visualViewport</c>). Distinct from
///     <see cref="ScreenInfo" /> (the physical display) and the layout viewport.
/// </summary>
/// <param name="Width">Visible width in CSS pixels.</param>
/// <param name="Height">Visible height in CSS pixels (shrinks when the soft keyboard shows).</param>
/// <param name="OffsetLeft">Left offset of the visual viewport from the layout viewport.</param>
/// <param name="OffsetTop">Top offset of the visual viewport from the layout viewport.</param>
/// <param name="PageLeft">X offset of the visual viewport from the document origin.</param>
/// <param name="PageTop">Y offset of the visual viewport from the document origin.</param>
/// <param name="Scale">Pinch-zoom scale (<c>1</c> at no zoom).</param>
public sealed record VisualViewport(
    double Width,
    double Height,
    double OffsetLeft,
    double OffsetTop,
    double PageLeft,
    double PageTop,
    double Scale);

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

/// <summary>
///     Default <see cref="IVisualViewport" />, backed by the unified <see cref="IJSRuntime" />.
///     <c>window.visualViewport</c> is a live object, so the read goes through the framework's
///     <c>__raskApi.visualViewport</c> helper, which returns a plain snapshot.
/// </summary>
public sealed class VisualViewportReader(IJSRuntime js) : IVisualViewport
{
    /// <inheritdoc />
    public ValueTask<bool> IsSupportedAsync() => js.InvokeAsync<bool>("__raskApi.visualViewportSupported");

    /// <inheritdoc />
    public ValueTask<VisualViewport?> GetAsync() =>
        js.InvokeAsync<VisualViewport?>("__raskApi.visualViewport");
}
