using static Company.RaskNative.Routes;

namespace Company.RaskNative;

// The root component — identical in shape to every other Rask host's App. It renders the full page shell
// (Doctype/Html/Head/Body, RASK021); on a native app that shell is morphed into the WebView on first paint.
public sealed class App : Component
{
    protected override Component? Head =>
    [
        Title()["Rask App"],
        Meta("utf-8"),
        Meta(Name: "viewport", Content: "width=device-width, initial-scale=1, viewport-fit=cover")
    ];

    protected override Component? Render() =>
    [
        Doctype(),
        Html("en")[
            Head(),
            // Pad the body by the device safe-area insets so content clears the status bar / notch /
            // home indicator (the boot shell requests an edge-to-edge viewport with viewport-fit=cover).
            Body(Style: "margin:0;padding:env(safe-area-inset-top) env(safe-area-inset-right) " +
                        "env(safe-area-inset-bottom) env(safe-area-inset-left)")[
                Nav()[
                    NavLink(HomePage())["Home"],
                    " | ",
                    NavLink(Counter())["Counter"]
                ],
                Hr(),
                Router()
            ]
        ]
    ];
}
