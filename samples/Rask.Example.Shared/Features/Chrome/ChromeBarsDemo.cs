using Rask.Chrome.Components;
using Rask.Core.Routing;

namespace Rask.Example.Shared.Features;

// AppBar and TabStrip are the PORTABLE chrome vocabulary (Rask.Chrome): one declaration that renders the
// landmark markup below on Server and WASM, and is projected to a real UINavigationBar + UITabBar (iOS) /
// top + bottom bar (Android) inside a native shell, where it emits no HTML at all.
//
// Composed inline here so the demo can sit in a doc; the same two components fill a Screen's HeaderBar and
// TabBar slots, which is what makes one screen class serve every host — see ChromeScreen at /chrome.
//
// Rask.Core ships no stylesheet for them on purpose: the rask-header-bar / rask-tab-bar / rask-bar-button
// class names ARE the styling contract, and the icon arrives as data-rask-icon="add" rather than a glyph so
// the framework carries no SVG payload and no icon-font dependency. ChromeBarsDemo.css is this app's half of
// that contract — scoped to this component, and about twenty lines.
public sealed partial class ChromeBarsDemo : Component
{
    private int _added;

    protected override Component? Render() =>
        Div.Class("chrome-demo")[
            AppBar
                .Title("Todos")
                .Trailing([BarButton.Icon(BarIcon.Add).Title("New").OnClick(() => _added++)]),
            Div.Class("chrome-demo-body")[
                P.Class("mb-0 small")[
                    "Tapped ",
                    Span.Id("chrome-added").Class("fw-semibold")[_added.ToString(
                        System.Globalization.CultureInfo.InvariantCulture)],
                    " time(s). On a native head the button above is a real bar item, and this text is the "
                    + "only thing inside the WebView."
                ]
            ],
            // Selected is left unset, so the lit tab is derived from the current route by the same method the
            // native host calls — one declaration cannot light different tabs on different hosts.
            TabStrip.Tabs([
                TabItem.Title("Home").Icon(BarIcon.Home).To(new RouteUrl("/")),
                TabItem.Title("Todos").Icon(BarIcon.List).To(Features.Routes.TodosPage()).Badge("3"),
                TabItem.Title("Me").Icon(BarIcon.Person).To(new RouteUrl("/me"))
            ])
        ];
}
