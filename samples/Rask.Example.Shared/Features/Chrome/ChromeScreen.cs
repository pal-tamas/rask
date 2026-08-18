using Rask.Chrome;
using Rask.Chrome.Components;
using Rask.Core.Routing;

namespace Rask.Example.Shared.Features;

// A Screen is a routed component that also declares the chrome AROUND it, instead of the app root inspecting
// the route to decide what the header should say. This one names no Rask.Native type — only Rask.Chrome — which is the
// whole point: the same class compiles and renders on all three hosts.
//
//   • Server / WASM  — the slots render landmark HTML (role="banner", role="navigation") into the page.
//   • Native         — the same declaration becomes a real UINavigationBar + UITabBar (iOS) / top + bottom
//                      bar (Android), and the screen contributes no markup for either.
//
// Unlisted in the sidebar, like /table: it exists so the cross-host claim is exercised by a real app that all
// three showcase hosts compile, not only by unit tests. Its styling comes from ChromeBarsDemo.css via the
// shared wrapper class — Rask.Core ships no stylesheet for the bars by design.
[Route("chrome")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed partial class ChromeScreen : Screen
{
    private int _refreshed;

    protected override Component? HeadAssets => Title["Portable chrome — Rask"];

    // Filled with Rask.Chrome components, so this compiles for the web heads. Reach for Rask.Native's
    // NativeHeaderBar instead when you want platform-exact chrome (segmented titles, overflow menus, per-bar
    // tinting) — and accept that the class then only builds for a native head.
    protected override Component? HeaderBar =>
        AppBar
            .Title("Portable chrome")
            .Trailing([BarButton.Icon(BarIcon.Add).Title("Refresh").OnClick(() => _refreshed++)]);

    // Declared on the screen rather than the layout here because there is one screen; a real app usually puts
    // the tab bar on a layout screen once and lets each leaf own its header. Chrome merges deepest-wins per
    // kind, so both survive.
    protected override Component? TabBar =>
        TabStrip.Tabs([
            TabItem.Title("Home").Icon(BarIcon.Home).To(new RouteUrl("/")),
            TabItem.Title("Todos").Icon(BarIcon.List).To(Features.Routes.TodosPage()),
            TabItem.Title("Chrome").Icon(BarIcon.Star).To(Features.Routes.ChromeScreen())
        ]);

    protected override Component? Render() =>
        Div.Class("chrome-demo")[
            Div.Class("chrome-demo-body")[
                PageHeader.Title("Portable chrome").Lead(
                    "One Screen class, three hosts. The bars above and below this text are landmark HTML "
                    + "here; on iOS and Android the same declaration is a real navigation bar and tab bar, "
                    + "and this paragraph is the only thing inside the WebView."),
                P.Class("small mb-0")[
                    "Refreshed ",
                    Span.Id("chrome-screen-refreshed").Class("fw-semibold")[
                        _refreshed.ToString(System.Globalization.CultureInfo.InvariantCulture)],
                    " time(s) — the bar button's callback attributes back to this screen and re-renders it "
                    + "exactly like a button in the body."
                ]
            ]
        ];
}
