namespace Rask.Example.Shared.Demos;

// The Rask brand mark: the lightning bolt from assets/rask-logo.svg, inlined as SVG.
// Inlined via Raw (not an <img src>) so it renders identically on the Server and WASM
// transports without duplicating an asset file across the two wwwroots.
internal static class RaskLogo
{
    // gradientId MUST be unique per call site on a given page — the navbar brand and the
    // home hero both render the mark on "/", and two <linearGradient> elements sharing an
    // id would collide (the second fill silently resolves to the first definition).
    public static Component Mark(double size, string gradientId)
    {
        var s = size.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return Raw(
            $"""
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="22 6 80 108" width="{s}" height="{s}" role="img" aria-label="Rask" focusable="false">
              <defs>
                <linearGradient id="{gradientId}" x1="0" y1="0" x2="1" y2="1">
                  <stop offset="0%" stop-color="#7C3AED"/>
                  <stop offset="100%" stop-color="#512BD4"/>
                </linearGradient>
              </defs>
              <path d="M72 14 L30 64 L54 64 L48 106 L94 54 L68 54 Z" fill="url(#{gradientId})"/>
            </svg>
            """);
    }
}
