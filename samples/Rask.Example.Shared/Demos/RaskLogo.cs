using System.Globalization;

namespace Rask.Example.Shared.Demos;

// The Rask brand mark: the lightning bolt from assets/rask-logo.svg, built from the core SVG
// components. Composed from typed factories (not Raw or an <img src>) so it renders identically
// on the Server and WASM transports without duplicating an asset file across the two wwwroots,
// and so the accessible name and gradient stops stay strongly typed.
internal static class RaskLogo
{
    // gradientId MUST be unique per call site on a given page — the navbar brand and the
    // home hero both render the mark on "/", and two <linearGradient> elements sharing an
    // id would collide (the second fill silently resolves to the first definition).
    public static Component Mark(double size, string gradientId)
    {
        var s = size.ToString(CultureInfo.InvariantCulture);
        return Svg(Width: s, Height: s, ViewBox: "22 6 80 108", Xmlns: "http://www.w3.org/2000/svg")[
            // A <title> child gives the mark its accessible name (the SVG-native equivalent of
            // aria-label) and demonstrates nesting under a shape/container element.
            SvgTitle()["Rask"],
            Defs()[
                LinearGradient(Id: gradientId, X1: "0", Y1: "0", X2: "1", Y2: "1")[
                    Stop(Offset: "0%", StopColor: "#7C3AED"),
                    Stop(Offset: "100%", StopColor: "#512BD4")
                ]
            ],
            SvgPath(D: "M72 14 L30 64 L54 64 L48 106 L94 54 L68 54 Z", Fill: $"url(#{gradientId})")
        ];
    }
}
