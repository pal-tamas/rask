using static Company.RaskNative.Routes;
using NativeIcon = Rask.Native.Components.NativeIcon;

// NativeHeaderBar / NativeTabBar / NativeTab / NativeWebView factories come from a global using the generator
// emits automatically for any project referencing Rask.Native — no `using static` needed here.

namespace Company.RaskNative;

// The root component. A native page is a small COMPOSED tree: the native bars (NativeHeaderBar / NativeTabBar)
// as siblings of a NativeWebView, which hosts the ordinary page shell (Doctype/Html/Head/Body, RASK021). The
// native host projects the bars to REAL platform chrome — a UINavigationBar + UITabBar on iOS, a top bar +
// bottom tab bar on Android — and serializes the NativeWebView's HTML into the WebView between them.
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
        // Real native top bar. Opt in by hosting webView.ChromeView + registering the head as INativeChrome —
        // see Platforms/iOS/AppDelegate.cs and Platforms/Android/MainActivity.cs.
        NativeHeaderBar(Title: "Rask App"),

        // The HTML surface — its children are the normal page shell, morphed into the platform WebView.
        NativeWebView()[
            Doctype(),
            Html("en")[
                Head(),
                // Pad the body by the device safe-area insets so content clears the status bar / notch /
                // home indicator (the boot shell requests an edge-to-edge viewport with viewport-fit=cover).
                Body(Style: "margin:0;padding:env(safe-area-inset-top) env(safe-area-inset-right) " +
                            "env(safe-area-inset-bottom) env(safe-area-inset-left)")[
                    Router()
                ]
            ]
        ],

        // Real native bottom tab bar — primary navigation. Tapping a tab routes to its type-safe To:.
        NativeTabBar(
            Tabs:
            [
                NativeTab(Title: "Home", Icon: NativeIcon.Home, To: HomePage()),
                NativeTab(Title: "Counter", Icon: NativeIcon.Add, To: Counter())
            ])
        // Selected is omitted — the framework highlights the tab matching the current route.
    ];
}
