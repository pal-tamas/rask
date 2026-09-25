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
