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
